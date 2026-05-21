`timescale 1ns / 1ps

module coordinate_mapper_tb;

    parameter W = 16;
    parameter F = 12;

    reg signed [W-1:0] x_min;
    reg signed [W-1:0] x_max;
    reg signed [W-1:0] y_min;
    reg signed [W-1:0] y_max;
    reg [7:0]         p;
    reg [6:0]         q;

    wire signed [W-1:0] x0;
    wire signed [W-1:0] y0;

    coordinate_mapper #(
        .IMG_W(160),
        .IMG_H(120),
        .W(W),
        .F(F)
    ) dut (
        .x_min(x_min),
        .x_max(x_max),
        .y_min(y_min),
        .y_max(y_max),
        .p(p),
        .q(q),
        .x0(x0),
        .y0(y0)
    );

    function real to_real;
        input signed [W-1:0] val;
        begin
            to_real = val / 4096.0;
        end
    endfunction

    initial begin
        $dumpfile("coordinate_mapper_tb.vcd");
        $dumpvars(0, coordinate_mapper_tb);

        // Map x from -4.0 to +4.0 and y from 0.0 to +6.0
        x_min = -16'sd16384;  // -4.0 in Q4.12
        x_max =  16'sd16384;  // +4.0
        y_min =  16'sd0;      // 0.0
        y_max =  16'sd24576;  // 6.0 in Q4.12

        #1;
        p = 0; q = 0;
        #1 $display("p=%0d q=%0d -> x0=%0d (%0f) y0=%0d (%0f)", p, q, x0, to_real(x0), y0, to_real(y0));

        p = 159; q = 119;
        #1 $display("p=%0d q=%0d -> x0=%0d (%0f) y0=%0d (%0f)", p, q, x0, to_real(x0), y0, to_real(y0));

        p = 80; q = 60;
        #1 $display("p=%0d q=%0d -> x0=%0d (%0f) y0=%0d (%0f)", p, q, x0, to_real(x0), y0, to_real(y0));

        p = 40; q = 30;
        #1 $display("p=%0d q=%0d -> x0=%0d (%0f) y0=%0d (%0f)", p, q, x0, to_real(x0), y0, to_real(y0));

        $display("coordinate_mapper_tb completed");
        #10 $finish;
    end

endmodule
