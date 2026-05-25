`timescale 1ns/1ps

module tb_framebuffer_colour_path;

    localparam int FB_W    = 160;
    localparam int FB_H    = 120;
    localparam int LABEL_W = 2;
    localparam int RGB_W   = 8;

    localparam int PIXELS  = FB_W * FB_H;
    localparam int ADDR_W  = $clog2(PIXELS);

    logic clk;
    logic rst;

    logic                 fb_wr_en;
    logic [ADDR_W-1:0]    fb_wr_addr;
    logic [LABEL_W-1:0]   fb_wr_data;

    logic [ADDR_W-1:0]    fb_rd_addr;
    logic                 active_video;

    logic [RGB_W-1:0]     red;
    logic [RGB_W-1:0]     green;
    logic [RGB_W-1:0]     blue;

    framebuffer_colour_path #(
        .FB_W(FB_W),
        .FB_H(FB_H),
        .LABEL_W(LABEL_W),
        .RGB_W(RGB_W)
    ) dut (
        .clk(clk),
        .rst(rst),

        .fb_wr_en(fb_wr_en),
        .fb_wr_addr(fb_wr_addr),
        .fb_wr_data(fb_wr_data),

        .fb_rd_addr(fb_rd_addr),
        .active_video(active_video),

        .red(red),
        .green(green),
        .blue(blue)
    );

    // 100 MHz clock
    initial begin
        clk = 1'b0;
        forever #5 clk = ~clk;
    end

    task automatic fail(input string msg);
        begin
            $display("ERROR: %s", msg);
            $finish;
        end
    endtask

    task automatic write_pixel(
        input logic [ADDR_W-1:0]  addr,
        input logic [LABEL_W-1:0] label
    );
        begin
            @(negedge clk);
            fb_wr_en   <= 1'b1;
            fb_wr_addr <= addr;
            fb_wr_data <= label;

            @(negedge clk);
            fb_wr_en   <= 1'b0;
            fb_wr_addr <= '0;
            fb_wr_data <= '0;
        end
    endtask

    task automatic read_rgb_check(
        input string              tc_name,
        input logic [ADDR_W-1:0]  addr,
        input logic               active_i,
        input logic [RGB_W-1:0]   exp_r,
        input logic [RGB_W-1:0]   exp_g,
        input logic [RGB_W-1:0]   exp_b
    );
        begin
            @(negedge clk);
            fb_rd_addr   <= addr;
            active_video <= active_i;

            // frame buffer has synchronous read,
            // and active_video is delayed by one clock inside the wrapper
            @(posedge clk);
            #1;

            if (red !== exp_r || green !== exp_g || blue !== exp_b) begin
                $display("%s failed at addr=%0d", tc_name, addr);
                $display("Expected RGB = (%0d, %0d, %0d)", exp_r, exp_g, exp_b);
                $display("Got      RGB = (%0d, %0d, %0d)", red, green, blue);
                $fatal;
            end else begin
                $display("PASS: %s addr=%0d RGB=(%0d,%0d,%0d)",
                         tc_name, addr, red, green, blue);
            end
        end
    endtask

    initial begin
        $dumpfile("tb_framebuffer_colour_path.vcd");
        $dumpvars(0, tb_framebuffer_colour_path);

        rst          = 1'b1;
        fb_wr_en     = 1'b0;
        fb_wr_addr   = '0;
        fb_wr_data   = '0;
        fb_rd_addr   = '0;
        active_video = 1'b0;

        repeat (3) @(posedge clk);

        @(negedge clk);
        rst = 1'b0;

        // ------------------------------------------------------------
        // Write known labels into frame buffer
        // ------------------------------------------------------------
        write_pixel(15'd0,     2'd1); // red
        write_pixel(15'd1,     2'd2); // green
        write_pixel(15'd2,     2'd3); // blue
        write_pixel(15'd1000,  2'd0); // black / unused
        write_pixel(15'd19199, 2'd1); // last valid pixel, red

        // ------------------------------------------------------------
        // Read back through colour encoder
        // ------------------------------------------------------------
        read_rgb_check("TC1 label 1 -> red",   15'd0,     1'b1, 8'hFF, 8'h00, 8'h00);
        read_rgb_check("TC2 label 2 -> green", 15'd1,     1'b1, 8'h00, 8'hFF, 8'h00);
        read_rgb_check("TC3 label 3 -> blue",  15'd2,     1'b1, 8'h00, 8'h00, 8'hFF);
        read_rgb_check("TC4 label 0 -> black", 15'd1000,  1'b1, 8'h00, 8'h00, 8'h00);
        read_rgb_check("TC5 last addr red",    15'd19199, 1'b1, 8'hFF, 8'h00, 8'h00);

        // ------------------------------------------------------------
        // active_video = 0 should force black
        // ------------------------------------------------------------
        read_rgb_check("TC6 inactive video -> black", 15'd0, 1'b0, 8'h00, 8'h00, 8'h00);

        $display("All framebuffer_colour_path tests passed.");
        #20;
        $finish;
    end

endmodule