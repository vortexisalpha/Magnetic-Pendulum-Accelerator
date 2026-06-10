//SIGNED FIXED POINT MULTIPLY BY 0.01 (DIVIDE BY 100) WITH SATURATION
// Q4.14 implementation - change W and F as required
// does a*0.01 and puts it in c

// THEORY:
// 41944 = 2^15 + 2^13 + 2^10 - 2^5 - 2^3
// so c = 41944 * a / 2^22 = 0.0100000095 * a

// PIPELINING:
// Pipelined over 2 stages to meet original fx_mul constraints.
// This allows for easy test/implementation of module

module fx_divide_by_100 #(
    parameter W = 18,
    parameter F = 14
)(
    input  logic                clk,
    input  logic                rst,
    input  logic signed [W-1:0] a,
    output logic signed [W-1:0] c
);
    //saturation limits
    //just to align with fx_mul i doubt we would need this
    localparam signed [W-1:0] SAT_MAX = {1'b0, {(W-1){1'b1}}};
    localparam signed [W-1:0] SAT_MIN = {1'b1, {(W-1){1'b0}}};

    //S1: build the product (a * 41944) and register
    //2*W bitwidth matches fx_mul implemetnation - max bit width for two muls is 2*W
    logic signed [2*W-1:0] product_r;
    wire  signed [2*W-1:0] a_ext = a; //sign extension to 2*W bits
    wire  signed [2*W-1:0] product_w = (a_ext <<< 15) + (a_ext <<< 13) + (a_ext <<< 10) - (a_ext <<<  5) - (a_ext <<<  3);


    //register accoridnglt
    always_ff @(posedge clk) begin
        if (rst) product_r <= '0;
        else     product_r <= product_w;
    end

    //S2: round + shift down and saturate
    // round 0.5 add 2^21 before shifting down by 22 bits
    wire signed [2*W-1:0] product_rnd = product_r + (1 <<< 21);
    wire signed [2*W-1:0] shifted     = product_rnd >>> 22;
    wire overflow  = (shifted > SAT_MAX);
    wire underflow = (shifted < SAT_MIN);
    assign c = overflow ? SAT_MAX : underflow ? SAT_MIN : shifted[W-1:0];

endmodule
