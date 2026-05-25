module nearest_magnet_s3 #(
    parameter W = 16,
    parameter F = 12,
    parameter Q_WIDTH = 18
)(
    input  logic clk,
    input  logic rst,
    input  logic in_valid,

    // Squared distances containing h^2 from Stage 3a
    input  logic [Q_WIDTH-1:0] q0,
    input  logic [Q_WIDTH-1:0] q1,
    input  logic [Q_WIDTH-1:0] q2,

    input  logic signed [W-1:0]       in_dx0, in_dy0, in_dx1, in_dy1, in_dx2, in_dy2,
    input  logic signed [W-1:0]       in_x, in_y,
    input  logic signed [W-1:0]       in_vx, in_vy,
    input  logic [11:0]               in_step_cnt,
    input  logic [14:0]               in_id,
    input  logic [1:0]                in_settle_count,

    // Outputs
    output logic                      out_valid,
    output logic [1:0]                nearest_magnet_id,
    output logic [Q_WIDTH-1:0] min_q,

    output logic signed [W-1:0]       out_dx0, out_dy0, out_dx1, out_dy1, out_dx2, out_dy2,
    output logic signed [W-1:0]       out_x, out_y,
    output logic [11:0]               out_step_cnt,
    output logic signed [W-1:0]       out_vx, out_vy,
    output logic [14:0]               out_id,
    output logic [1:0]                out_settle_count,

    output  logic [Q_WIDTH-1:0] out_q0,
    output  logic [Q_WIDTH-1:0] out_q1,
    output  logic [Q_WIDTH-1:0] out_q2
);

    // ── registered outputs ────────────────────────────────────────────────────
    always_ff @(posedge clk) begin
        if (rst) begin
            nearest_magnet_id <= 2'd0;
            min_q <= {Q_WIDTH{1'd0}};
            out_valid <= 1'd0;
        end
        else begin

            //pass through values
            out_valid <= in_valid;
            out_dx0 <= in_dx0;
            out_dy0 <= in_dy0;
            out_dx1 <= in_dx1;
            out_dy1 <= in_dy1;
            out_dx2 <= in_dx2;
            out_dy2 <= in_dy2;
            out_x <= in_x;
            out_y <= in_y;
            out_vx <= in_vx;
            out_vy <= in_vy;
            out_step_cnt <= in_step_cnt;
            out_id <= in_id;
            out_settle_count <= in_settle_count;

            out_q0 <= q0;
            out_q1 <= q1;
            out_q2 <= q2;

            if (in_valid) begin
                if (q0 <= q1 && q0 <= q2) begin
                    nearest_magnet_id <= 2'd1;
                    min_q <= q0;
                end
                else if (q1 <= q2) begin
                    nearest_magnet_id <= 2'd2;
                    min_q <= q1;
                end
                else begin
                    nearest_magnet_id <= 2'd3;
                    min_q <= q2;
                end
            end
            else begin
                nearest_magnet_id <= 2'd0;
                min_q <= '0;
            end
        end
    end

endmodule