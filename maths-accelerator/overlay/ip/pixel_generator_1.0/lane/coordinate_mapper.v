
module coordinate_mapper #(
    parameter IMG_W   = 160,
    parameter IMG_H   = 120,
    parameter W       = 16,
    parameter F       = 12
)(
    input  signed [W-1:0]           x_min,
    input  signed [W-1:0]           x_max,
    input  signed [W-1:0]           y_min,
    input  signed [W-1:0]           y_max,

    input        [7:0]              p,
    input        [6:0]              q,

    output signed [W-1:0]           x0,
    output signed [W-1:0]           y0
);

    localparam signed [15:0] INV_W = 16'sd409;  
    localparam signed [15:0] INV_H = 16'sd546;  

    wire signed [W-1:0] x_range = x_max - x_min;
    wire signed [W-1:0] y_range = y_max - y_min;

    wire signed [31:0] x_step_full = x_range * INV_W;
    wire signed [31:0] y_step_full = y_range * INV_H;

    // Shift right 16 bits to convert back to Q4.12
    wire signed [W-1:0] x_step = x_step_full[31:16];
    wire signed [W-1:0] y_step = y_step_full[31:16];

    // Multiply integer pixel coordinate by step value. Result stays Q4.12.
    wire signed [W+8:0] p_offset_full = $signed({1'b0, p}) * x_step;
    wire signed [W+7:0] q_offset_full = $signed({1'b0, q}) * y_step;

    wire signed [W-1:0] p_offset = p_offset_full[W-1:0];
    wire signed [W-1:0] q_offset = q_offset_full[W-1:0];

    assign x0 = x_min + p_offset;
    assign y0 = y_min + q_offset;

endmodule
