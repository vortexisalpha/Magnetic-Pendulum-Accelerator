module frame_buffer #(
    parameter FB_W   = 160,
    parameter FB_H   = 120,
    parameter DATA_W = 6, // stores step cnt categorized and magnet id

    parameter PIXELS = FB_W * FB_H, // 160 * 120
    parameter ADDR_W = $clog2(PIXELS)
)(
    input  logic    clk,
    input  logic    rst,

    // Write port: from settling/timeout checker FSM
    input  logic    wr_en,
    input  logic [ADDR_W-1:0]   wr_addr,
    input  logic [DATA_W-1:0]   wr_data,

    // Read port: debug/display 
    input  logic [ADDR_W-1:0]   rd_addr,
    output logic [DATA_W-1:0]   rd_data
);

    // Ask Vivado to infer block RAM
    (* ram_style = "block" *)
    logic [DATA_W-1:0] mem [0:PIXELS-1];

    always_ff @(posedge clk) begin
        if (rst) begin
            rd_data <= '0;
        end 
        
        else begin
            // Write finished pixel result
            if (wr_en && (wr_addr < PIXELS)) begin
                mem[wr_addr] <= wr_data;    
            end

            // Synchronous read
            if (rd_addr < PIXELS) begin
                rd_data <= mem[rd_addr];
            end 
            
            else begin
                rd_data <= '0;
            end
        end
    end

endmodule