//SIGNED FIXED POINT MULTIPLY W SATURATION 
// Q4.14 implementation - change W and F as required
//does a*b and puts it in c

//ISSUE:
// an issue during synthesis of the design on vivado for 8 lanes showed that
// the design was not meeting timing constraints due to the pure
// combinational logic in stage 2. This was resolved by pipelining the
// design across 2 stages, with the first stage registering the raw product
// and second stage doing the shift + saturation. This means that all stages
// that use this fx_mul block would need an additional register stage

module fx_mul #(
    parameter W = 18,
    parameter F = 14
)(
    input  logic              clk,
    input  logic              rst,
    input  logic signed [W-1:0] a,
    input  logic signed [W-1:0] b,
    output logic signed [W-1:0] c
);
    //saturation limits
    localparam signed [W-1:0] SAT_MAX = {1'b0, {(W-1){1'b1}}};
    localparam signed [W-1:0] SAT_MIN = {1'b1, {(W-1){1'b0}}};

    //S1: multiply and put raw product into reg
    logic signed [2*W-1:0] product_r;
    always_ff @(posedge clk) begin
        if (rst) product_r <= '0;
        else      product_r <= a * b;
    end

    //S2: shift + saturateion happens here
    wire signed [2*W-1-F:0] shifted = product_r >>> F;
    wire overflow  = (shifted > SAT_MAX);
    wire underflow = (shifted < SAT_MIN);
    assign c = overflow ? SAT_MAX : underflow ? SAT_MIN : shifted[W-1:0];

endmodule
