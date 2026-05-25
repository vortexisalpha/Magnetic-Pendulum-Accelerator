module coordinate_mapper #(
    parameter IMG_W = 160,
    parameter IMG_H = 120,
    parameter W = 16,
    parameter F = 12,

    parameter P_W = $clog2(IMG_W),
    parameter Q_W = $clog2(IMG_H)
)(
    input  logic  clk,
    input  logic  rst,

    input  logic  valid_in,

    input  logic signed [W-1:0]  x_min,
    input  logic signed [W-1:0]  y_min,
    input  logic signed [W-1:0]  x_step,
    input  logic signed [W-1:0]  y_step,

    input  logic [P_W-1:0]  p,
    input  logic [Q_W-1:0]  q,

    output logic  valid_out,
    output logic signed [W-1:0]  x0,
    output logic signed [W-1:0]  y0,
    output logic init_step_cnt,
    output logic init_settle_cnt
);

    // p and q are unsigned pixel indices.
    // x_step and y_step are signed fixed-point values.
    localparam int P_MUL_W = P_W + W + 1;
    localparam int Q_MUL_W = Q_W + W + 1;

    logic signed [P_MUL_W-1:0] p_offset_full;
    logic signed [Q_MUL_W-1:0] q_offset_full;
    
    logic signed [W-1:0] p_offset_q;
    logic signed [W-1:0] q_offset_q;

    always_comb begin
        p_offset_full = $signed({1'b0, p}) * x_step;
        q_offset_full = $signed({1'b0, q}) * y_step;

        // Keep lower W bits, assuming the result fits in the chosen Q format.
        p_offset_q = p_offset_full[W-1:0];
        q_offset_q = q_offset_full[W-1:0];
    end

    always_ff @(posedge clk) begin
        if (rst) begin
            valid_out <= 1'b0;
            x0 <= '0;
            y0 <= '0;
        end 
        else begin
            valid_out <= valid_in;

            if (valid_in) begin
                x0 <= x_min + p_offset_q;
                y0 <= y_min + q_offset_q;
                init_step_cnt <= 1'b0;
                init_settle_cnt <= 1'b0;
            end
        end
    end

endmodule
