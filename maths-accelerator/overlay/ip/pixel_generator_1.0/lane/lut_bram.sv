module lut_bram #(
    parameter W = 18,
    parameter F = 14,
    parameter LUT_SIZE = 8192, // 8192 entries
    parameter LUT_ADDR_W = 13  // 2^13 = 8192
)(
    input clk,
    input rst,
    input logic [W-1:0] addr,
    output logic signed [W-1:0] data_out
);

    logic [W-1:0] rom_array [0:LUT_SIZE-1];

    initial begin
        $display("loading ROM contents...");
        $readmemh("qinv32.mem", rom_array);
    end
    logic [LUT_ADDR_W-1:0] idx;

    // no saturation needed
    assign idx = addr[17:5];

    always @(posedge clk) begin
        //synchronous read
        data_out <= rom_array[idx];
    end
endmodule