`timescale 1ns/1ps

module tb_frame_buffer;

    localparam int FB_W   = 160;
    localparam int FB_H   = 120;
    localparam int DATA_W = 2;
    localparam int PIXELS = FB_W * FB_H;
    localparam int ADDR_W = $clog2(PIXELS);

    logic clk;
    logic rst;

    logic              wr_en;
    logic [ADDR_W-1:0] wr_addr;
    logic [DATA_W-1:0] wr_data;

    logic [ADDR_W-1:0] rd_addr;
    logic [DATA_W-1:0] rd_data;

    frame_buffer #(
        .FB_W(FB_W),
        .FB_H(FB_H),
        .DATA_W(DATA_W)
    ) dut (
        .clk(clk),
        .rst(rst),

        .wr_en(wr_en),
        .wr_addr(wr_addr),
        .wr_data(wr_data),

        .rd_addr(rd_addr),
        .rd_data(rd_data)
    );

    initial begin
        clk = 1'b0;
        forever #5 clk = ~clk;
    end

    task automatic write_pixel(
        input logic [ADDR_W-1:0] addr,
        input logic [DATA_W-1:0] data
    );
        begin
            @(negedge clk);
            wr_en   = 1'b1;
            wr_addr = addr;
            wr_data = data;

            @(negedge clk);
            wr_en   = 1'b0;
            wr_addr = '0;
            wr_data = '0;
        end
    endtask

    task automatic read_check(
        input logic [ADDR_W-1:0] addr,
        input logic [DATA_W-1:0] expected
    );
        begin
            @(negedge clk);
            rd_addr = addr;

            @(posedge clk);
            #1;

            if (rd_data !== expected) begin
                $display("ERROR: addr=%0d, expected=%0d, got=%0d",
                         addr, expected, rd_data);
                $fatal;
            end else begin
                $display("PASS: addr=%0d, data=%0d", addr, rd_data);
            end
        end
    endtask

    initial begin
        rst     = 1'b1;
        wr_en   = 1'b0;
        wr_addr = '0;
        wr_data = '0;
        rd_addr = '0;

        repeat (3) @(posedge clk);
        rst = 1'b0;

        write_pixel(0,     2'd0);
        write_pixel(1,     2'd1);
        write_pixel(2,     2'd2);
        write_pixel(1000,  2'd3);
        write_pixel(19199, 2'd2);

        read_check(0,     2'd0);
        read_check(1,     2'd1);
        read_check(2,     2'd2);
        read_check(1000,  2'd3);
        read_check(19199, 2'd2);

        $display("All frame buffer tests passed.");
        $finish;
    end

endmodule