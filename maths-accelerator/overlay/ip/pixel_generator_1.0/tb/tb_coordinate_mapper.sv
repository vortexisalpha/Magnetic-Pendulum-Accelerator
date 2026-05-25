`timescale 1ns/1ps

module tb_coordinate_mapper;

    localparam int IMG_W = 160;
    localparam int IMG_H = 120;
    localparam int W     = 16;
    localparam int F     = 12;

    localparam int P_W   = $clog2(IMG_W);
    localparam int Q_W   = $clog2(IMG_H);

    // Q4.12 fixed-point constants.
    // real value = stored integer / 4096.
    localparam int signed X_MIN_I  = -8192;  // -2.0
    localparam int signed Y_MIN_I  = -6144;  // -1.5

    // Approximate step size.
    // 103 / 4096 ≈ 0.02515
    // This keeps the last pixel inside Q4.12 range.
    localparam int signed X_STEP_I = 103;
    localparam int signed Y_STEP_I = 103;

    logic clk;
    logic rst;
    logic valid_in;

    logic signed [W-1:0] x_min;
    logic signed [W-1:0] y_min;
    logic signed [W-1:0] x_step;
    logic signed [W-1:0] y_step;

    logic [P_W-1:0] p;
    logic [Q_W-1:0] q;

    logic valid_out;
    logic signed [W-1:0] x0;
    logic signed [W-1:0] y0;

    coordinate_mapper #(
        .IMG_W(IMG_W),
        .IMG_H(IMG_H),
        .W(W),
        .F(F)
    ) dut (
        .clk(clk),
        .rst(rst),
        .valid_in(valid_in),

        .x_min(x_min),
        .y_min(y_min),
        .x_step(x_step),
        .y_step(y_step),

        .p(p),
        .q(q),

        .valid_out(valid_out),
        .x0(x0),
        .y0(y0)
    );

    // 100 MHz clock: 10 ns period
    initial begin
        clk = 1'b0;
        forever #5 clk = ~clk;
    end

    task automatic check_mapper(
        input logic [P_W-1:0] p_in,
        input logic [Q_W-1:0] q_in
    );
        int signed expected_x;
        int signed expected_y;
        begin
            expected_x = X_MIN_I + int'(p_in) * X_STEP_I;
            expected_y = Y_MIN_I + int'(q_in) * Y_STEP_I;

            @(negedge clk);
            valid_in = 1'b1;
            p        = p_in;
            q        = q_in;

            // coordinate_mapper has one-cycle latency.
            @(posedge clk);
            #1;

            if (valid_out !== 1'b1) begin
                $display("ERROR: valid_out not high for p=%0d q=%0d", p_in, q_in);
                $fatal;
            end

            if ($signed(x0) !== expected_x) begin
                $display("ERROR: x0 mismatch for p=%0d", p_in);
                $display("       expected x0 = %0d, got x0 = %0d", expected_x, $signed(x0));
                $fatal;
            end

            if ($signed(y0) !== expected_y) begin
                $display("ERROR: y0 mismatch for q=%0d", q_in);
                $display("       expected y0 = %0d, got y0 = %0d", expected_y, $signed(y0));
                $fatal;
            end

            $display("PASS: p=%0d q=%0d -> x0=%0d y0=%0d",
                     p_in, q_in, $signed(x0), $signed(y0));
        end
    endtask

    initial begin
        rst      = 1'b1;
        valid_in = 1'b0;

        x_min  = X_MIN_I[W-1:0];
        y_min  = Y_MIN_I[W-1:0];
        x_step = X_STEP_I[W-1:0];
        y_step = Y_STEP_I[W-1:0];

        p = '0;
        q = '0;

        repeat (3) @(posedge clk);
        rst = 1'b0;

        // Check reset behaviour
        @(posedge clk);
        #1;
        if (valid_out !== 1'b0) begin
            $display("ERROR: valid_out should be 0 after reset.");
            $fatal;
        end

        // Basic coordinate checks
        check_mapper(0,   0);
        check_mapper(1,   0);
        check_mapper(0,   1);
        check_mapper(10,  20);
        check_mapper(80,  60);
        check_mapper(159, 119);

        // Check valid_in = 0 behaviour
        @(negedge clk);
        valid_in = 1'b0;
        p        = 8'd5;
        q        = 7'd5;

        @(posedge clk);
        #1;

        if (valid_out !== 1'b0) begin
            $display("ERROR: valid_out should be 0 when valid_in is 0.");
            $fatal;
        end

        $display("All coordinate mapper tests passed.");
        $finish;
    end

endmodule