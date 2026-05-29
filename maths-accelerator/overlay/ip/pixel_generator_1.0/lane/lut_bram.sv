module lut_bram #(
    parameter W = 18,
    parameter F = 14,
    parameter LUT_SIZE = 4096, // 4096 entries
    parameter LUT_ADDR_W = 12  // 2^12 = 4096
)(
    input clk,
    input rst,
    input logic [17:0] addr,
    output logic signed [W-1:0] data_out
);

    logic [W-1:0] rom_array [0:LUT_SIZE-1];

    initial begin
        $display("loading ROM contents...");
        $readmemh("qinv32.mem", rom_array);
    end
    logic [LUT_ADDR_W-1:0] idx;

    // addr[17:6] gives 12-bit index covering q_real in [0, 32)
    assign idx = addr[LUT_ADDR_W+5:6];

    always @(posedge clk) begin
        //synchronous read
        data_out <= rom_array[idx];
    end
endmodule
