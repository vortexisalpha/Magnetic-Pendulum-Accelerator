//SIGNED FIXED POINT ADD WITHOUT SATURATION 
// Q5.13 implementation - change W and F as required
//does a+b+c and puts it in d

module fx_adder_s3 #(
    parameter W = 18,
    parameter F = 14
)(
    input  signed [W-1:0] a,
    input  signed [W-1:0] b,
    input  signed [W-1:0] c,
    // q is a W-bit UNSIGNED Q5.13 value
    output [W-1:0] d
);
    // a, b, c are non-negative Q4.14 values (but we are using signed for continuity with rest of code)
    // to prevent overflow during addition, we will represent raw sum with W+2 bits.
    wire [W+1:0] sum_q414 = $unsigned(a) + $unsigned(b) + $unsigned(c);

    // Q4.14 -> Q5.13 conversion
    wire [W:0] q_q513 = sum_q414[W+1:1];

    // saturation logic
    assign d = q_q513[W] ? {W{1'b1}} : q_q513[W-1:0];
endmodule
