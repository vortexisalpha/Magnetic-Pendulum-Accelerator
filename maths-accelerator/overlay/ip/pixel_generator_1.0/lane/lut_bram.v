module lut_bram #(
    parameter W = 16,
    parameter F = 12,
    parameter LUT_SIZE = 1024, //1024 entries
    parameter LUT_ADDR_W = 10 // 2^10 = 1024
)(
    input clk,
    input rst,
    input logic signed [W-1:0] addr,
    output logic signed [W-1:0] data_out
);
    
    logic [W-1:0] rom_array [0:LUT_SIZE-1];
    
    initial begin
        $display("loading ROM contents...");
        $readmemh("qinv32.mem", rom_array);
    end
    
    
    logic [LUT_ADDR_W-1:0] idx;
    

    //index = q*128
    assign idx = addr[LUT_ADDR_W+4:5];
    
    always @(posedge clk) begin
        //synchronous read
        data_out <= rom_array[idx];
    end
endmodule