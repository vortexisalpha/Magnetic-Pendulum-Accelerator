module lut_bram #(
    parameter W = 18,
    parameter F = 14,
    parameter Q_WIDTH = 18,
    parameter LUT_SIZE = 4096, // 4096 entries
    parameter LUT_ADDR_W = 12  // 2^12 = 4096
)(
    input clk,
    input rst,
    // q is unsiged and is always greater than h2
    input  logic [Q_WIDTH-1:0]  q,
    // h2 in Q4.14
    input  logic signed [W-1:0] h2,
    output logic signed [W-1:0] data_out
);
    //q now covers q in [h2, h2 + 8) as per david's request
    //THis is change from q being in [0, 32) range, which means we have
    //around 4x finer resoluion.
    //index is offset from h2 and right shifted to match the step.
    localparam int LUT_SHIFT   = 4;

    logic [Q_WIDTH-1:0] h2_q5;
    logic [Q_WIDTH-1:0] diff;
    logic [Q_WIDTH-1:0] shifted;
    logic [LUT_ADDR_W-1:0] idx;

    //convert h2 to same format as q
    assign h2_q5   = h2 >> 1;
    //find diff
    assign diff    = (q > h2_q5) ? (q - h2_q5) : {Q_WIDTH{1'b0}};
    
    //shift to get index as step size is now 2^13 and ratio with 16 is 512 = 8 (8 total units)/4096
    assign shifted = diff >> LUT_SHIFT;

    //clamping
    assign idx     = (shifted >= LUT_SIZE) ? LUT_ADDR_W'(LUT_SIZE - 1) : shifted[LUT_ADDR_W-1:0];

    logic [W-1:0] rom_array [0:LUT_SIZE-1];

    initial begin
        $display("loading ROM contents...");
        $readmemh("qinv32.mem", rom_array);
    end

    always @(posedge clk) begin
        //synchronous read
        data_out <= rom_array[idx];
    end
endmodule