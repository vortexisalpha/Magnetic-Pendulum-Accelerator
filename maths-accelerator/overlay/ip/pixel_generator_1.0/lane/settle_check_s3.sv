module settle_check_s3 #(
    parameter W = 16,
    parameter F = 12,
    parameter Q_WIDTH = 19
)(
    input  logic clk,
    input  logic rst,
    input  logic in_valid,

    input  logic signed [W-1:0]       in_dx0, in_dy0, in_dx1, in_dy1, in_dx2, in_dy2,
    input  logic signed [W-1:0]       in_x, in_y,
    input  logic signed [W-1:0]       in_vx, in_vy,
    input  logic [11:0]               in_step_cnt,
    input  logic [14:0]               in_id,
    input  logic [1:0]                in_settle_count,

    input  logic [1:0]                in_nearest_magnet_id,
    input  logic signed [Q_WIDTH-1:0]        min_q,

    input  logic signed [Q_WIDTH-1:0]        sum_r_settle_sq_h_sq, // r_settle^2 + h^2
    // also writing this signed here because q values were written as signed values, for consistency, this is also written as signed, same as min_q
    // might be best to write all squared and abs values as unsigned later

    input  logic [W-1:0]       v_settle,

    input  logic signed [Q_WIDTH-1:0] in_q0,
    input  logic signed [Q_WIDTH-1:0] in_q1,
    input  logic signed [Q_WIDTH-1:0] in_q2,

    // Outputs
    output logic                      out_valid,
    output logic signed [W-1:0]       out_dx0, out_dy0, out_dx1, out_dy1, out_dx2, out_dy2,
    output logic signed [W-1:0]       out_x, out_y,
    output logic [11:0]               out_step_cnt,
    output logic signed [W-1:0]       out_vx, out_vy,
    output logic [14:0]               out_id,
    output logic [1:0]                out_nearest_magnet_id,
    output logic [1:0]                out_settle_count,

    output  logic signed [Q_WIDTH-1:0] out_q0,
    output  logic signed [Q_WIDTH-1:0] out_q1,
    output  logic signed [Q_WIDTH-1:0] out_q2
);

    logic [W-1:0]        abs_vx, abs_vy;

    always_comb begin
        abs_vx = in_vx[W-1] ? -in_vx : in_vx;
        abs_vy = in_vy[W-1] ? -in_vy : in_vy;
    end

    always_ff @(posedge clk) begin
        if (rst) begin
            out_settle_count <= 2'd0;
            out_nearest_magnet_id <= 2'd0;
            out_valid <= 1'd0;

            // might be best to set the rest of the values to 0 as well?
            // currently consistent with the style of lane.sv, but might consider rst also sets the pass thru vals to 0

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
            
            out_nearest_magnet_id <= in_nearest_magnet_id;

            out_q0 <= in_q0;
            out_q1 <= in_q1;
            out_q2 <= in_q2;

            if (in_valid &&
                (min_q  < sum_r_settle_sq_h_sq) &&
                (abs_vx < v_settle)    &&
                (abs_vy < v_settle))
                // saturate at 3 to prevent 2-bit overflow
                out_settle_count <= (in_settle_count == 2'd3) ? 2'd3
                                                               : in_settle_count + 2'd1;
            else
                out_settle_count <= 2'd0;
        end
    end

endmodule