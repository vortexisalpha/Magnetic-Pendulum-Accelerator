`timescale 1ns / 1ps

module coordinate_mapper_tb;

    parameter W = 16;
    parameter F = 12;

    reg clk;

    reg signed [W-1:0] x_min;
    reg signed [W-1:0] x_max;
    reg signed [W-1:0] y_min;
    reg signed [W-1:0] y_max;

    reg signed [W-1:0] x_step;
    reg signed [W-1:0] y_step;

    reg [7:0] p;
    reg [6:0] q;

    wire signed [W-1:0] x0;
    wire signed [W-1:0] y0;

    coordinate_mapper #(
        .IMG_W(160),
        .IMG_H(120),
        .W(W),
        .F(F)
    ) dut (
        .clk(clk),
        .x_min(x_min),
        .x_max(x_max),
        .y_min(y_min),
        .y_max(y_max),
        .x_step(x_step),
        .y_step(y_step),
        .p(p),
        .q(q),
        .x0(x0),
        .y0(y0)
    );

    // 100 MHz clock
    always #5 clk = ~clk;

    function real to_real;
        input signed [W-1:0] val;
        begin
            to_real = val / 4096.0;
        end
    endfunction

    initial begin

        clk = 0;

        // Physical coordinate ranges
        x_min = -16'sd16384;   // -4.0
        x_max =  16'sd16384;   // +4.0

        y_min =  16'sd0;       // 0.0
        y_max =  16'sd24576;   // 6.0

        // step = range / resolution
        // x_step = 8.0 / 160 = 0.05
        // y_step = 6.0 / 120 = 0.05

        x_step = 16'sd205;     // 0.05 * 4096
        y_step = 16'sd205;

        // TEST 1
        p = 0;
        q = 0;

        #10;

        $display("TEST1");
        $display("p=%0d q=%0d -> x0=%0d (%0f) y0=%0d (%0f)",
                 p, q, x0, to_real(x0), y0, to_real(y0));

        // TEST 2
        p = 159;
        q = 119;

        #10;

        $display("TEST2");
        $display("p=%0d q=%0d -> x0=%0d (%0f) y0=%0d (%0f)",
                 p, q, x0, to_real(x0), y0, to_real(y0));

        // TEST 3
        p = 80;
        q = 60;

        #10;

        $display("TEST3");
        $display("p=%0d q=%0d -> x0=%0d (%0f) y0=%0d (%0f)",
                 p, q, x0, to_real(x0), y0, to_real(y0));

        #20;
        $finish;
    end

endmodule
