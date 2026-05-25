//SIGNED FIXED POINT ADD WITHOUT SATURATION 
// Q4.12 implementation - change W and F as required
//does a+b+c and puts it in d

module fx_adder_s3 #(
    parameter W = 16,
    parameter F = 12
)(
    input  signed [W-1:0] a,
    input  signed [W-1:0] b,
    input  signed [W-1:0] c,
    output signed [W+1:0] d
);
    //add a and b and c and put it into sum
    wire signed [W+1:0] sum;
    assign sum = a + b + c;

    //assign appropriately:
    assign d = sum;
endmodule