module lut_bram #(
    parameter W = 18,
    parameter F = 14,
    parameter LUT_SIZE = 4096, // 4096 entries
    parameter LUT_ADDR_W = 12  // 2^12 = 4096
)(
    input clk,
    input rst,
    input logic [W+1:0] addr,
    output logic signed [W-1:0] data_out
);

    logic [W-1:0] rom_array [0:LUT_SIZE-1];

    initial begin
        $display("loading ROM contents...");
        $readmemh("qinv32.mem", rom_array);
    end
    logic [LUT_ADDR_W-1:0] idx;

    //saturate address to LUT SIZE, if addr exceed LUT_SIZE, use last entry
    // if bit 19 is 1, use last entry (highest idx possible), else, use bit 18 to 7
    assign idx = (addr[W+1]) ? {LUT_ADDR_W{1'b1}} : addr[LUT_ADDR_W+6:7];

    always @(posedge clk) begin
        //synchronous read
        data_out <= rom_array[idx];
    end
endmodule