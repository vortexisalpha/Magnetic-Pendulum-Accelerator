module lut_bram #(
    parameter W = 16,
    parameter F = 12,
    parameter LUT_SIZE = 1024, //1024 entries
    parameter LUT_ADDR_W = 10 // 2^10 = 1024
)(
    input clk,
    input rst,
    input logic [W-1:0] addr,
    output logic signed [W-1:0] data_out
);

logic [W-1:0] rom_array [LUT_SIZE-1:0];

initial begin
    $display("loading ROM contents...");
    $readmemh("q^-3/2.mem", rom_array);
end


logic [LUT_ADDR_W-1:0] idx;

//we only use the lower LUT_ADDR_W bits of the address to index into the ROM
//this would be inaccurate depending on how the address is generated
//further testing is required - either increase rom size or add logic
assign idx = addr[LUT_ADDR_W-1:0];

always @(posedge clk) begin
    //synchronous read
    data_out <= rom_array[idx];
end
endmodule