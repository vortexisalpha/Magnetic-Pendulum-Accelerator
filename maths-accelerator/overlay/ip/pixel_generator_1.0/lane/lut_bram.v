module lut_bram #(
    parameter W = 16,
    parameter F = 12,
    parameter LUT_SIZE = 1024, //1024 entries
    parameter LUT_ADDR_W = 10 // 2^10 = 1024
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
    

    //possibly do (q - qmin) * LUT SIZE / q(max - qmin) = idx
    // but this uses 2 DSPS - check accuracy
    
    logic [LUT_ADDR_W-1:0] idx;

    // index = q_raw >> 5 = q * 128, clamped to 1023
    //use a bunch of ors here
    assign idx = (|addr[W+1:LUT_ADDR_W+5]) ? {LUT_ADDR_W{1'b1}} : addr[LUT_ADDR_W+4:5];
    
    always @(posedge clk) begin
        //synchronous read
        data_out <= rom_array[idx];
    end
endmodule