//SIGNED FIXED POINT sub W SATURATION 
// Q4.12 implementation - change W and F as required
//does a-b and puts it in c

module fx_sub_two_input #(
    parameter W = 16,
    parameter F = 12
)(
    input  signed [W-1:0] a,
    input  signed [W-1:0] b,
    output signed [W-1:0] c
);
    //subtract b from a and put it into sum
    wire signed [W:0] product;
    assign product = a - b;

    //define saturation limits
    localparam signed [W-1:0] SAT_MAX = {1'b0, {(W-1){1'b1}}};  // 0x7FFFFF (01111111)
    localparam signed [W-1:0] SAT_MIN = {1'b1, {(W-1){1'b0}}};  // 0x800000 (100000000)

    //check for overflow or underflow - therefore saturate
    //you could also just look at the bit values in W and W+1 but
    //i feel like this is more straightforward - i don't know if
    //vivado would simplify this but is a TODO for later optimization
    wire overflow  = (product > SAT_MAX);
    wire underflow = (product < SAT_MIN);

    //assign appropriately:
    assign c = (overflow)  ? SAT_MAX : (underflow) ? SAT_MIN : product[W-1:0];
endmodule