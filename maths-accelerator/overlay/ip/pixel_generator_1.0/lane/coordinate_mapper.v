
module coordinate_mapper #(
    parameter IMG_W = 160,
    parameter IMG_H = 120,
    parameter W     = 16,
    parameter F     = 12
)(
    input                       clk,
    input  signed [W-1:0]       x_min,
    input  signed [W-1:0]       x_max,
    input  signed [W-1:0]       y_min,
    input  signed [W-1:0]       y_max,
    input  signed [W-1:0]       x_step,  
    input  signed [W-1:0]       y_step,  
    input         [7:0]         p,
    input         [6:0]         q,
    output reg signed [W-1:0]   x0,
    output reg signed [W-1:0]   y0
);

    wire signed [W+8:0] p_offset_full = $signed({1'b0, p}) * x_step;
    
    wire signed [W+7:0] q_offset_full = $signed({1'b0, q}) * y_step;

    always @(posedge clk) begin
        x0 <= x_min + p_offset_full[W-1:0];
        y0 <= y_min + q_offset_full[W-1:0];
    end

endmodule