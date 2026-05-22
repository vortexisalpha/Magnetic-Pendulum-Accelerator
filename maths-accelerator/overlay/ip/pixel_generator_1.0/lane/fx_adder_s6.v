//SIGNED FIXED POINT ADD W SATURATION 
// Q4.12 implementation - change W and F as required
//does a+b+c and puts it in d

module fx_adder_s6 #(
    parameter W = 16,
    parameter F = 12
)(
    input  signed [W-1:0] a,
    input  signed [W-1:0] b,
    input  signed [W-1:0] c,
    output signed [W-1:0] d
);
    //add a and b and c and put it into sum
    wire signed [W+1:0] sum;
    assign sum = a + b + c;

    //define saturation limits
    localparam signed [W-1:0] SAT_MAX = {1'b0, {(W-1){1'b1}}};  // 0x7FFFFF (01111111)
    localparam signed [W-1:0] SAT_MIN = {1'b1, {(W-1){1'b0}}};  // 0x800000 (100000000)

    //check for overflow or underflow - therefore saturate
    //you could also just look at the bit values in W and W+1 but
    //i feel like this is more straightforward - i don't know if
    //vivado would simplify this but is a TODO for later optimization
    wire overflow  = (sum > SAT_MAX);
    wire underflow = (sum < SAT_MIN);

    //assign appropriately:
    assign d = (overflow)  ? SAT_MAX : (underflow) ? SAT_MIN : sum[W-1:0];
endmodule