//SIGNED FIXED POINT MULTIPLY W SATURATION 
// Q4.12 implementation - change W and F as required
//does a*b and puts it in c

module fx_mul #(
    parameter W = 16,
    parameter F = 12
)(
    input  signed [W-1:0] a,
    input  signed [W-1:0] b,
    output signed [W-1:0] c
);

    //multiply and put it into product
    wire signed [2*W-1:0] product;
    assign product = a * b;
    
    //shift the product down by F to match correct fractoral representation
    wire signed [2*W-1-F:0] shifted;
    assign shifted = product >>> F;

    //define saturation limits
    localparam signed [W-1:0] SAT_MAX = {1'b0, {(W-1){1'b1}}};  // 0x7FFFFF (01111111)
    localparam signed [W-1:0] SAT_MIN = {1'b1, {(W-1){1'b0}}};  // 0x800000 (100000000)

    //check for overflow or underflow - therefore saturate
    wire overflow  = (shifted > SAT_MAX);
    wire underflow = (shifted < SAT_MIN);

    //assign appropriately:
    assign c = (overflow)  ? SAT_MAX : (underflow) ? SAT_MIN : shifted[W-1:0];
endmodule