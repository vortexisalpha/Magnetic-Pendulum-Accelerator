module coordinate_mapper #(
    parameter IMG_W = 160,
    parameter IMG_H = 120,
    parameter W = 18,
    parameter F = 14,

    parameter P_W = $clog2(IMG_W),
    parameter Q_W = $clog2(IMG_H),
    parameter PIXEL_ID_W = $clog2(IMG_W * IMG_H)
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
    input  logic [PIXEL_ID_W-1:0]  pixel_id,

    output logic  valid_out,
    output logic  busy,            // high while a coordinate occupies the pipeline
    output logic signed [W-1:0]  x0,
    output logic signed [W-1:0]  y0,
    output logic init_step_cnt,
    output logic init_settle_cnt,
    output logic [PIXEL_ID_W-1:0]  pixel_id_out
);
    logic signed [W-1:0] p_offset_c;
    logic signed [W-1:0] q_offset_c;

    integer i;
    always_comb begin
        p_offset_c = '0;
        for (i = 0; i < P_W; i = i + 1)
            if (p[i]) p_offset_c = p_offset_c + (x_step <<< i);

        q_offset_c = '0;
        for (i = 0; i < Q_W; i = i + 1)
            if (q[i]) q_offset_c = q_offset_c + (y_step <<< i);
    end
    
    logic                   s1_valid;
    logic signed [W-1:0]    s1_p_offset, s1_q_offset;
    logic signed [W-1:0]    s1_x_min, s1_y_min;
    logic [PIXEL_ID_W-1:0]  s1_pixel_id;

    always_ff @(posedge clk) begin
        if (rst) begin
            s1_valid <= 1'b0;
        end
        else begin
            s1_valid    <= valid_in;
            s1_p_offset <= p_offset_c;
            s1_q_offset <= q_offset_c;
            s1_x_min    <= x_min;       // sample origin alongside the offset
            s1_y_min    <= y_min;
            s1_pixel_id <= pixel_id;
        end
    end

    always_ff @(posedge clk) begin
        if (rst) begin
            valid_out <= 1'b0;
            x0 <= '0;
            y0 <= '0;
        end
        else begin
            valid_out <= s1_valid;

            if (s1_valid) begin
                x0 <= s1_x_min + s1_p_offset;
                y0 <= s1_y_min + s1_q_offset;
                init_step_cnt   <= 1'b0;
                init_settle_cnt <= 1'b0;
                pixel_id_out    <= s1_pixel_id;
            end
        end
    end

    // A coordinate is in flight if it occupies either pipeline stage. The scanner
    // must wait for this to clear before issuing the next pixel (the old design
    // gated on valid_out only, which is no longer sufficient with 2-cycle latency).
    assign busy = s1_valid | valid_out;

endmodule
