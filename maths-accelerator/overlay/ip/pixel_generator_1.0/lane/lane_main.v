module lane_main #(
    parameter W = 16,
    parameter F = 12,
    parameter LUT_SIZE = 1024, //1024 entries
    parameter LUT_ADDR_W = 10, // 2^10 = 1024
    parameter Q_WIDTH = 18 // Q 7.12 representation, covers roughly -64 to 63
)(
    input clk,
    input rst,
    //========================================================================================
    //                  Physical paramater inputs
    //========================================================================================

    input logic signed [W-1:0] mag0_x, mag0_y,
    input logic signed [W-1:0] mag1_x, mag1_y,
    input logic signed [W-1:0] mag2_x, mag2_y,

    input logic signed [W-1:0] gamma, omega2, h2, mu, dt,

    //not signed as it's always positive
    input logic [W-1:0] r_settle_sq, v_settle,

    
    // new declarations
    // also need input declarations of r_settle_sq_h2_sum, v_settle,
    input logic [Q_WIDTH-1:0] sum_r_settle_sq_h_sq,

    //========================================================================================
    //                  pixel info
    //========================================================================================
    input logic pixel_valid,
    input logic signed [W-1:0] pixel_x, pixel_y, pixel_vx, pixel_vy,
    input logic [11:0] pixel_step_cnt, //12 bits = 4096 steps max
    input logic [14:0] pixel_id, //15 bits = 32768 pixels max and therefore fits inside 160x120

    //used to count how many times the pixel has 'settled'
    input logic [1:0] settle_count,


    //========================================================================================
    //                  outputs
    //========================================================================================
    output logic out_valid,
    output logic signed [W-1:0] out_x, out_y, out_vx, out_vy,
    output logic [11:0] out_step_cnt,
    output logic [14:0] out_id,
    output logic [1:0] out_magnet_id,

    output logic [1:0] out_settle_count
);

//========================================================================================
//                  S1: compute dx and dy (0 dsps)
//========================================================================================

//outputs:
logic signed [W-1:0] s1_dx0, s1_dy0, s1_dx1, s1_dy1, s1_dx2, s1_dy2;
logic signed [W-1:0] s1_dx0_w, s1_dy0_w, s1_dx1_w, s1_dy1_w, s1_dx2_w, s1_dy2_w;
logic signed [W-1:0] s1_x, s1_y, s1_vx, s1_vy;
logic [11:0] s1_step_cnt;
logic [14:0] s1_id;
logic s1_valid;

logic [1:0] s1_settle_count;

fx_sub_two_input #(.W(W), .F(F)) s1_sub_dx0 (.a(mag0_x),.b(pixel_x),.c(s1_dx0_w));
fx_sub_two_input #(.W(W), .F(F)) s1_sub_dy0 (.a(mag0_y),.b(pixel_y),.c(s1_dy0_w));
fx_sub_two_input #(.W(W), .F(F)) s1_sub_dx1 (.a(mag1_x),.b(pixel_x),.c(s1_dx1_w));
fx_sub_two_input #(.W(W), .F(F)) s1_sub_dy1 (.a(mag1_y),.b(pixel_y),.c(s1_dy1_w));
fx_sub_two_input #(.W(W), .F(F)) s1_sub_dx2 (.a(mag2_x),.b(pixel_x),.c(s1_dx2_w));
fx_sub_two_input #(.W(W), .F(F)) s1_sub_dy2 (.a(mag2_y),.b(pixel_y),.c(s1_dy2_w));


always @(posedge clk) begin
    if (rst) begin
        s1_valid <= 0;
    end
    else begin
        s1_valid <= pixel_valid;
        s1_dx0 <= s1_dx0_w;
        s1_dy0 <= s1_dy0_w;
        s1_dx1 <= s1_dx1_w;
        s1_dy1 <= s1_dy1_w;
        s1_dx2 <= s1_dx2_w;
        s1_dy2 <= s1_dy2_w;
        s1_x <= pixel_x;
        s1_y <= pixel_y;
        s1_vx <= pixel_vx;
        s1_vy <= pixel_vy;
        s1_step_cnt <= pixel_step_cnt;
        s1_id <= pixel_id;
        s1_settle_count <= settle_count;
    end
end

//========================================================================================
//                  S2: compute dx^2 and dy^2 (6 dsps)
//========================================================================================

//outputs:
logic signed [W-1:0] s2_dx0, s2_dy0, s2_dx1, s2_dy1, s2_dx2, s2_dy2;
logic signed [W-1:0] s2_dx0_sq, s2_dy0_sq, s2_dx1_sq, s2_dy1_sq, s2_dx2_sq, s2_dy2_sq;
logic signed [W-1:0] s2_x, s2_y, s2_vx, s2_vy;
logic [11:0] s2_step_cnt;
logic [14:0] s2_id;
logic s2_valid;

logic [1:0] s2_settle_count;

//combinatorial outputs for fx_mul modules
logic signed [W-1:0] s2_dx0_sq_w, s2_dy0_sq_w, s2_dx1_sq_w, s2_dy1_sq_w, s2_dx2_sq_w, s2_dy2_sq_w;

//square dx and dy values per magnet (6 total) using fx_mul module - 6 DSPs used
fx_mul #(.W(W), .F(F)) s2_m_dx0 (.a(s1_dx0),.b(s1_dx0),.c(s2_dx0_sq_w));
fx_mul #(.W(W), .F(F)) s2_m_dy0 (.a(s1_dy0),.b(s1_dy0),.c(s2_dy0_sq_w));
fx_mul #(.W(W), .F(F)) s2_m_dx1 (.a(s1_dx1),.b(s1_dx1),.c(s2_dx1_sq_w));
fx_mul #(.W(W), .F(F)) s2_m_dy1 (.a(s1_dy1),.b(s1_dy1),.c(s2_dy1_sq_w));
fx_mul #(.W(W), .F(F)) s2_m_dx2 (.a(s1_dx2),.b(s1_dx2),.c(s2_dx2_sq_w));
fx_mul #(.W(W), .F(F)) s2_m_dy2 (.a(s1_dy2),.b(s1_dy2),.c(s2_dy2_sq_w));

always @(posedge clk) begin
    if (rst) begin
        s2_valid <= 0;
    end
    else begin
        //pass through values
        s2_valid <= s1_valid;
        s2_dx0 <= s1_dx0;
        s2_dy0 <= s1_dy0;
        s2_dx1 <= s1_dx1;
        s2_dy1 <= s1_dy1;
        s2_dx2 <= s1_dx2;
        s2_dy2 <= s1_dy2;
        s2_x <= s1_x;
        s2_y <= s1_y;
        s2_vx <= s1_vx;
        s2_vy <= s1_vy;
        s2_step_cnt <= s1_step_cnt;
        s2_id <= s1_id;
        s2_settle_count <= s1_settle_count;

        //register squared outputs for pipline
        s2_dx0_sq <= s2_dx0_sq_w;
        s2_dy0_sq <= s2_dy0_sq_w;
        s2_dx1_sq <= s2_dx1_sq_w;
        s2_dy1_sq <= s2_dy1_sq_w;
        s2_dx2_sq <= s2_dx2_sq_w;
        s2_dy2_sq <= s2_dy2_sq_w;
    end
end

//========================================================================================
//                  S3a: q = dx^2 + dy^2 + h^2
//========================================================================================
// outputs


logic [Q_WIDTH-1:0] s3a_q0, s3a_q1, s3a_q2;

logic [Q_WIDTH-1:0] s3a_q0_w, s3a_q1_w, s3a_q2_w;

logic signed [W-1:0] s3a_dx0, s3a_dy0, s3a_dx1, s3a_dy1, s3a_dx2, s3a_dy2;

logic signed [W-1:0] s3a_x, s3a_y, s3a_vx, s3a_vy;
logic [11:0] s3a_step_cnt;
logic [14:0] s3a_id;
logic s3a_valid;

logic [1:0] s3a_settle_count;

fx_adder_s3 #(.W(W), .F(F)) s3_q0_adder (.a(s2_dx0_sq), .b(s2_dy0_sq), .c(h2), .d(s3a_q0_w));
fx_adder_s3 #(.W(W), .F(F)) s3_q1_adder (.a(s2_dx1_sq), .b(s2_dy1_sq), .c(h2), .d(s3a_q1_w));
fx_adder_s3 #(.W(W), .F(F)) s3_q2_adder (.a(s2_dx2_sq), .b(s2_dy2_sq), .c(h2), .d(s3a_q2_w));


always_ff @(posedge clk) begin
    if (rst) begin
        s3a_valid <= 0;
        s3a_settle_count <= 2'd0;
    end
    
    else begin

        s3a_q0 <= s3a_q0_w;
        s3a_q1 <= s3a_q1_w;
        s3a_q2 <= s3a_q2_w;

        //pass through values
        s3a_valid <= s2_valid;
        s3a_dx0 <= s2_dx0;
        s3a_dy0 <= s2_dy0;
        s3a_dx1 <= s2_dx1;
        s3a_dy1 <= s2_dy1;
        s3a_dx2 <= s2_dx2;
        s3a_dy2 <= s2_dy2;
        s3a_x <= s2_x;
        s3a_y <= s2_y;
        s3a_vx <= s2_vx;
        s3a_vy <= s2_vy;
        s3a_step_cnt <= s2_step_cnt;
        s3a_id <= s2_id;
        s3a_settle_count <= s2_settle_count;
    end
end

//========================================================================================
//                  S3b: nearest magnet select
//========================================================================================
// outputs
logic                       s3b_valid;
logic [1:0]                 s3b_nearest_magnet_id;
logic [Q_WIDTH-1:0]  s3b_min_q;

logic signed [W-1:0] s3b_dx0, s3b_dy0, s3b_dx1, s3b_dy1, s3b_dx2, s3b_dy2;
logic signed [W-1:0] s3b_x, s3b_y, s3b_vx, s3b_vy;
logic [11:0]         s3b_step_cnt;
logic [14:0]         s3b_id;
logic [1:0]          s3b_settle_count;

logic [Q_WIDTH-1:0]  s3b_q0, s3b_q1, s3b_q2;

nearest_magnet_s3 #(
    .W(W),
    .F(F),
    .Q_WIDTH(Q_WIDTH)
) nearest_magnet_stage3b (
    .clk(clk),
    .rst(rst),
    .in_valid(s3a_valid),

    .q0(s3a_q0),
    .q1(s3a_q1),
    .q2(s3a_q2),

    .in_dx0(s3a_dx0),
    .in_dy0(s3a_dy0),
    .in_dx1(s3a_dx1),
    .in_dy1(s3a_dy1),
    .in_dx2(s3a_dx2),
    .in_dy2(s3a_dy2),

    .in_x(s3a_x),
    .in_y(s3a_y),
    .in_vx(s3a_vx),
    .in_vy(s3a_vy),

    .in_step_cnt(s3a_step_cnt),
    .in_id(s3a_id),
    .in_settle_count(s3a_settle_count),

    .out_valid(s3b_valid),
    .nearest_magnet_id(s3b_nearest_magnet_id),
    .min_q(s3b_min_q),

    .out_dx0(s3b_dx0),
    .out_dy0(s3b_dy0),
    .out_dx1(s3b_dx1),
    .out_dy1(s3b_dy1),
    .out_dx2(s3b_dx2),
    .out_dy2(s3b_dy2),

    .out_x(s3b_x),
    .out_y(s3b_y),
    .out_step_cnt(s3b_step_cnt),

    .out_vx(s3b_vx),
    .out_vy(s3b_vy),

    .out_id(s3b_id),
    .out_settle_count(s3b_settle_count),

    .out_q0(s3b_q0),
    .out_q1(s3b_q1),
    .out_q2(s3b_q2)
);

//========================================================================================
//                  S3c: settle check
//========================================================================================
// outputs
logic                       s3c_valid;
logic signed [W-1:0]        s3c_dx0, s3c_dy0, s3c_dx1, s3c_dy1, s3c_dx2, s3c_dy2;
logic signed [W-1:0]        s3c_x, s3c_y, s3c_vx, s3c_vy;
logic [11:0]                s3c_step_cnt;
logic [14:0]                s3c_id;
logic [1:0]                 s3c_nearest_magnet_id;
logic [1:0]                 s3c_settle_count;

logic [Q_WIDTH-1:0] s3c_q0, s3c_q1, s3c_q2;

settle_check_s3 #(
    .W(W),
    .F(F),
    .Q_WIDTH(Q_WIDTH)
) settle_check_stage3c (
    .clk(clk),
    .rst(rst),
    .in_valid(s3b_valid),

    .in_dx0(s3b_dx0),
    .in_dy0(s3b_dy0),
    .in_dx1(s3b_dx1),
    .in_dy1(s3b_dy1),
    .in_dx2(s3b_dx2),
    .in_dy2(s3b_dy2),

    .in_x(s3b_x),
    .in_y(s3b_y),
    .in_vx(s3b_vx),
    .in_vy(s3b_vy),

    .in_step_cnt(s3b_step_cnt),
    .in_id(s3b_id),
    .in_settle_count(s3b_settle_count),

    .in_nearest_magnet_id(s3b_nearest_magnet_id),
    .min_q(s3b_min_q),

    .sum_r_settle_sq_h_sq(sum_r_settle_sq_h_sq),
    .v_settle(v_settle),

    .in_q0(s3b_q0),
    .in_q1(s3b_q1),
    .in_q2(s3b_q2),

    .out_valid(s3c_valid),

    .out_dx0(s3c_dx0),
    .out_dy0(s3c_dy0),
    .out_dx1(s3c_dx1),
    .out_dy1(s3c_dy1),
    .out_dx2(s3c_dx2),
    .out_dy2(s3c_dy2),

    .out_x(s3c_x),
    .out_y(s3c_y),

    .out_step_cnt(s3c_step_cnt),

    .out_vx(s3c_vx),
    .out_vy(s3c_vy),

    .out_id(s3c_id),

    .out_nearest_magnet_id(s3c_nearest_magnet_id),
    .out_settle_count(s3c_settle_count),

    .out_q0(s3c_q0),
    .out_q1(s3c_q1),
    .out_q2(s3c_q2)
);

//========================================================================================
//                  S4a: q^-3/2 (invq) calcution via LUT (0 DSP)
//========================================================================================

//outputs
logic signed [W-1:0] s4a_invq0, s4a_invq1, s4a_invq2;
logic signed [W-1:0] s4a_dx0, s4a_dy0, s4a_dx1, s4a_dy1, s4a_dx2, s4a_dy2;
logic signed [W-1:0] s4a_x, s4a_y, s4a_vx, s4a_vy;
logic [11:0] s4a_step_cnt;
logic [14:0] s4a_id;
logic s4a_valid;

logic [1:0] s4a_magnet_id;

logic [1:0] s4a_settle_count;

lut_bram #(.W(W), .F(F), .LUT_SIZE(LUT_SIZE), .LUT_ADDR_W(LUT_ADDR_W)) lut0 (
    .clk(clk),
    .rst(rst),
    .addr(s3c_q0),
    .data_out(s4a_invq0)
);

lut_bram #(.W(W), .F(F), .LUT_SIZE(LUT_SIZE), .LUT_ADDR_W(LUT_ADDR_W)) lut1 (
    .clk(clk),
    .rst(rst),
    .addr(s3c_q1),
    .data_out(s4a_invq1)
);

lut_bram #(.W(W), .F(F), .LUT_SIZE(LUT_SIZE), .LUT_ADDR_W(LUT_ADDR_W)) lut2 (
    .clk(clk),
    .rst(rst),
    .addr(s3c_q2),
    .data_out(s4a_invq2)
);

always @(posedge clk) begin
    if (rst) begin
        s4a_valid <= 0;
    end
    else begin
        //pass through values
        s4a_valid <= s3c_valid;
        s4a_dx0 <= s3c_dx0;
        s4a_dy0 <= s3c_dy0;
        s4a_dx1 <= s3c_dx1;
        s4a_dy1 <= s3c_dy1;
        s4a_dx2 <= s3c_dx2;
        s4a_dy2 <= s3c_dy2;
        s4a_x <= s3c_x;
        s4a_y <= s3c_y;
        s4a_vx <= s3c_vx;
        s4a_vy <= s3c_vy;
        s4a_step_cnt <= s3c_step_cnt;
        s4a_id <= s3c_id;
        s4a_settle_count <= s3c_settle_count;
        s4a_magnet_id <= s3c_nearest_magnet_id;
    end
end

//========================================================================================
//                  S4b: LUT buffer stage for timing
//========================================================================================
//outputs
logic signed [W-1:0] s4b_invq0, s4b_invq1, s4b_invq2;
logic signed [W-1:0] s4b_dx0, s4b_dy0, s4b_dx1, s4b_dy1, s4b_dx2, s4b_dy2;
logic signed [W-1:0] s4b_x, s4b_y, s4b_vx, s4b_vy;
logic [11:0] s4b_step_cnt;
logic [14:0] s4b_id;
logic s4b_valid;

logic [1:0] s4b_magnet_id;

logic [1:0] s4b_settle_count;

always @(posedge clk) begin
    if (rst) begin
        s4b_valid <= 0;
    end
    else begin
        s4b_invq0 <= s4a_invq0;
        s4b_invq1 <= s4a_invq1;
        s4b_invq2 <= s4a_invq2;
        
        //pass through values
        s4b_valid <= s4a_valid;
        s4b_dx0 <= s4a_dx0;
        s4b_dy0 <= s4a_dy0;
        s4b_dx1 <= s4a_dx1;
        s4b_dy1 <= s4a_dy1;
        s4b_dx2 <= s4a_dx2;
        s4b_dy2 <= s4a_dy2;
        s4b_x <= s4a_x;
        s4b_y <= s4a_y;
        s4b_vx <= s4a_vx;
        s4b_vy <= s4a_vy;
        s4b_step_cnt <= s4a_step_cnt;
        s4b_id <= s4a_id;
        s4b_settle_count <= s4a_settle_count;
        s4b_magnet_id <= s4a_magnet_id;
    end
end
//========================================================================================
//                  S5: multiply dx, dy with qinv (6 DSPs)
//========================================================================================
//outputs
logic signed [W-1:0] s5_dx_invq0, s5_dx_invq1, s5_dx_invq2;
logic signed [W-1:0] s5_dy_invq0, s5_dy_invq1, s5_dy_invq2;
logic signed [W-1:0] s5_x, s5_y, s5_vx, s5_vy;
logic [11:0] s5_step_cnt;
logic [14:0] s5_id;
logic s5_valid;
logic [1:0] s5_magnet_id;

logic [1:0] s5_settle_count;

//intermediate combinatorial outputs for fx_mul modules
logic signed [W-1:0] s5_dx_invq0_w, s5_dx_invq1_w, s5_dx_invq2_w;
logic signed [W-1:0] s5_dy_invq0_w, s5_dy_invq1_w, s5_dy_invq2_w;

//dx, dy values multipled with qinv - 6 DSPs used
fx_mul #(.W(W), .F(F)) s5_m_dx0 (.a(s4b_dx0),.b(s4b_invq0),.c(s5_dx_invq0_w));
fx_mul #(.W(W), .F(F)) s5_m_dy0 (.a(s4b_dy0),.b(s4b_invq0),.c(s5_dy_invq0_w));
fx_mul #(.W(W), .F(F)) s5_m_dx1 (.a(s4b_dx1),.b(s4b_invq1),.c(s5_dx_invq1_w));
fx_mul #(.W(W), .F(F)) s5_m_dy1 (.a(s4b_dy1),.b(s4b_invq1),.c(s5_dy_invq1_w));
fx_mul #(.W(W), .F(F)) s5_m_dx2 (.a(s4b_dx2),.b(s4b_invq2),.c(s5_dx_invq2_w));
fx_mul #(.W(W), .F(F)) s5_m_dy2 (.a(s4b_dy2),.b(s4b_invq2),.c(s5_dy_invq2_w));

always @(posedge clk) begin
    if (rst) begin
        s5_valid <= 0;
    end
    else begin
        //pass through values
        s5_valid <= s4b_valid;
        s5_x <= s4b_x;
        s5_y <= s4b_y;
        s5_vx <= s4b_vx;
        s5_vy <= s4b_vy;
        s5_step_cnt <= s4b_step_cnt;
        s5_id <= s4b_id;
        // register invq products so they align w pipeline
        s5_dx_invq0 <= s5_dx_invq0_w;
        s5_dy_invq0 <= s5_dy_invq0_w;
        s5_dx_invq1 <= s5_dx_invq1_w;
        s5_dy_invq1 <= s5_dy_invq1_w;
        s5_dx_invq2 <= s5_dx_invq2_w;
        s5_dy_invq2 <= s5_dy_invq2_w;
        s5_settle_count <= s4b_settle_count;
        s5_magnet_id <= s4b_magnet_id;
    end
end

//========================================================================================
//            S6a: multiply gamma_vel, omega2x, sum all dy dx with qinv multiplied
//========================================================================================
//outputs

logic signed [W-1:0] s6a_x, s6a_y, s6a_vx, s6a_vy;
logic [11:0] s6a_step_cnt;
logic [14:0] s6a_id;
logic s6a_valid;
logic [1:0] s6a_magnet_id;

logic [1:0] s6a_settle_count;

//intermediate sums and combinatorial multiplier outputs
logic signed [W-1:0] dx_invq_sum_w, dy_invq_sum_w;
logic signed [W-1:0] gamma_vel_x_w, gamma_vel_y_w;
logic signed [W-1:0] omega2_pos_x_w, omega2_pos_y_w;

//for pipeline
logic signed [W-1:0] s6a_dx_invq_sum, s6a_dy_invq_sum;
logic signed [W-1:0] s6a_gamma_vel_x, s6a_gamma_vel_y;
logic signed [W-1:0] s6a_omega2_pos_x, s6a_omega2_pos_y;



//multiply w physical paramaters
fx_mul #(.W(W), .F(F)) m_gamma_vel_x (.a(gamma), .b(s5_vx), .c(gamma_vel_x_w));
fx_mul #(.W(W), .F(F)) m_omega2_pos_x (.a(omega2), .b(s5_x), .c(omega2_pos_x_w));
fx_mul #(.W(W), .F(F)) m_gamma_vel_y (.a(gamma), .b(s5_vy), .c(gamma_vel_y_w));
fx_mul #(.W(W), .F(F)) m_omega2_pos_y (.a(omega2), .b(s5_y), .c(omega2_pos_y_w));

fx_adder_s6 #(.W(W), .F(F)) dx_invq_adder (.a(s5_dx_invq0), .b(s5_dx_invq1), .c(s5_dx_invq2), .d(dx_invq_sum_w));
fx_adder_s6 #(.W(W), .F(F)) dy_invq_adder (.a(s5_dy_invq0), .b(s5_dy_invq1), .c(s5_dy_invq2), .d(dy_invq_sum_w));

always @(posedge clk) begin
    if (rst) begin
        s6a_valid <= 0;
    end
    else begin
        //pass through values
        s6a_valid <= s5_valid;
        s6a_x <= s5_x;
        s6a_y <= s5_y;
        s6a_vx <= s5_vx;
        s6a_vy <= s5_vy;
        s6a_step_cnt <= s5_step_cnt;
        s6a_id <= s5_id;
        s6a_settle_count <= s5_settle_count;
        s6a_magnet_id <= s5_magnet_id;
        //registers for alignment of combinatorial outputs
        s6a_dx_invq_sum <= dx_invq_sum_w;
        s6a_dy_invq_sum <= dy_invq_sum_w;

        s6a_gamma_vel_x <= gamma_vel_x_w;
        s6a_omega2_pos_x <= omega2_pos_x_w;

        s6a_gamma_vel_y <= gamma_vel_y_w;
        s6a_omega2_pos_y <= omega2_pos_y_w;
    end
end


//========================================================================================
//            S6b: multiply dx/dy_invq_sum with mu
//========================================================================================
//outputs
logic signed [W-1:0] s6b_x, s6b_y, s6b_vx, s6b_vy;
logic [11:0] s6b_step_cnt;
logic [14:0] s6b_id;
logic s6b_valid;

logic [1:0] s6b_settle_count;
logic [1:0] s6b_magnet_id;

logic signed [W-1:0] mu_dx_invq, mu_dy_invq;
logic signed [W-1:0] mu_dx_invq_w, mu_dy_invq_w;

logic signed [W-1:0] s6b_gamma_vel_x, s6b_gamma_vel_y;
logic signed [W-1:0] s6b_omega2_pos_x, s6b_omega2_pos_y;


fx_mul #(.W(W), .F(F)) m_mu_dx_invq (.a(mu), .b(s6a_dx_invq_sum), .c(mu_dx_invq_w));
fx_mul #(.W(W), .F(F)) m_mu_dy_invq (.a(mu), .b(s6a_dy_invq_sum), .c(mu_dy_invq_w));

always @(posedge clk) begin
    if (rst) begin
        s6b_valid <= 0;
    end
    else begin
        //pass through values
        s6b_valid <= s6a_valid;
        s6b_x <= s6a_x;
        s6b_y <= s6a_y;
        s6b_vx <= s6a_vx;
        s6b_vy <= s6a_vy;
        s6b_step_cnt <= s6a_step_cnt;
        s6b_id <= s6a_id;
        s6b_settle_count <= s6a_settle_count;
        s6b_magnet_id <= s6a_magnet_id;

        s6b_gamma_vel_x <= s6a_gamma_vel_x;
        s6b_omega2_pos_x <= s6a_omega2_pos_x;
        s6b_gamma_vel_y <= s6a_gamma_vel_y;
        s6b_omega2_pos_y <= s6a_omega2_pos_y;

        //register ax and ay for pipeline alignment
        mu_dx_invq <= mu_dx_invq_w;
        mu_dy_invq <= mu_dy_invq_w;
    end
end

//========================================================================================
//            S6c: subtract mu_dx/dy_invq_w with gamma_vel and omega2_pos
//========================================================================================
//outputs
logic signed [W-1:0] s6c_x, s6c_y, s6c_vx, s6c_vy;
logic [11:0] s6c_step_cnt;
logic [14:0] s6c_id;
logic s6c_valid;
logic [1:0] s6c_magnet_id;

logic [1:0] s6c_settle_count;

logic signed [W-1:0] s6c_ax, s6c_ay;
logic signed [W-1:0] s6c_ax_w, s6c_ay_w;

fx_sub_three_input #(.W(W), .F(F)) s6c_ax_subtractor (.a(mu_dx_invq), .b(s6b_gamma_vel_x), .c(s6b_omega2_pos_x), .d(s6c_ax_w));
fx_sub_three_input #(.W(W), .F(F)) s6c_ay_subtractor (.a(mu_dy_invq), .b(s6b_gamma_vel_y), .c(s6b_omega2_pos_y), .d(s6c_ay_w));

always @(posedge clk) begin
    if (rst) begin
        s6c_valid <= 0;
    end
    else begin
        //pass through values
        s6c_valid <= s6b_valid;
        s6c_x <= s6b_x;
        s6c_y <= s6b_y;
        s6c_vx <= s6b_vx;
        s6c_vy <= s6b_vy;
        s6c_step_cnt <= s6b_step_cnt;
        s6c_id <= s6b_id;
        s6c_settle_count <= s6b_settle_count;
        s6c_magnet_id <= s6b_magnet_id;

        //register ax and ay for pipeline alignment
        s6c_ax <= s6c_ax_w;
        s6c_ay <= s6c_ay_w;
    end
end


//========================================================================================
//                  S7a: find dt_ax and dt_ay
//========================================================================================
//outputs

logic signed [W-1:0] s7a_x, s7a_y, s7a_vx, s7a_vy;
logic [11:0] s7a_step_cnt;
logic [14:0] s7a_id;
logic s7a_valid;
logic [1:0] s7a_magnet_id;

logic [1:0] s7a_settle_count;

logic signed [W-1:0] s7a_dt_ax, s7a_dt_ay;
logic signed [W-1:0] s7a_dt_ax_w, s7a_dt_ay_w;

fx_mul #(.W(W), .F(F)) m_dt_ax (.a(dt),.b(s6c_ax),.c(s7a_dt_ax_w));
fx_mul #(.W(W), .F(F)) m_dt_ay (.a(dt),.b(s6c_ay),.c(s7a_dt_ay_w));


always @(posedge clk) begin
    if (rst) begin
        s7a_valid <= 0;
    end
    else begin
        //update velocity with dt_ax and dt_ay
        s7a_dt_ax <= s7a_dt_ax_w;
        s7a_dt_ay <= s7a_dt_ay_w;

        //pass through values
        s7a_valid <= s6c_valid;
        s7a_x <= s6c_x;
        s7a_y <= s6c_y;
        s7a_vx <= s6c_vx;
        s7a_vy <= s6c_vy;
        s7a_step_cnt <= s6c_step_cnt;
        s7a_id <= s6c_id;

        s7a_magnet_id <= s6c_magnet_id;
        s7a_settle_count <= s6c_settle_count;
    end
end

//========================================================================================
//                  S7b: update new value of v
//========================================================================================
//outputs

logic signed [W-1:0] s7b_x, s7b_y, s7b_vx, s7b_vy;
logic [11:0] s7b_step_cnt;
logic [14:0] s7b_id;
logic s7b_valid;
logic [1:0] s7b_magnet_id;

logic [1:0] s7b_settle_count;

logic signed [W-1:0] s7b_vx_w, s7b_vy_w;

fx_adder_two_input #(.W(W), .F(F)) s7b_vx_adder (.a(s7a_vx), .b(s7a_dt_ax), .c(s7b_vx_w));
fx_adder_two_input #(.W(W), .F(F)) s7b_vy_adder (.a(s7a_vy), .b(s7a_dt_ay), .c(s7b_vy_w));

always @(posedge clk) begin
    if (rst) begin
        s7b_valid <= 0;
    end
    else begin
        //update velocity with dt_ax and dt_ay
        s7b_vx <= s7b_vx_w;
        s7b_vy <= s7b_vy_w;

        //pass through values
        s7b_valid <= s7a_valid;
        s7b_x <= s7a_x;
        s7b_y <= s7a_y;
        s7b_step_cnt <= s7a_step_cnt;
        s7b_id <= s7a_id;
        s7b_magnet_id <= s7a_magnet_id;

        s7b_settle_count <= s7a_settle_count;
    end
end


//========================================================================================
//                  S8a: update new value of x and y
//========================================================================================
//outputs
logic signed [W-1:0] s8a_x, s8a_y, s8a_vx, s8a_vy;
logic [11:0] s8a_step_cnt;
logic [14:0] s8a_id;
logic s8a_valid;
logic [1:0] s8a_settle_count;
logic [1:0] s8a_magnet_id;

logic signed [W-1:0] s8a_dt_x, s8a_dt_y;
logic signed [W-1:0] s8a_dt_x_w, s8a_dt_y_w;

fx_mul #(.W(W), .F(F)) m_dt_x (.a(dt),.b(s7b_vx),.c(s8a_dt_x_w));
fx_mul #(.W(W), .F(F)) m_dt_y (.a(dt),.b(s7b_vy),.c(s8a_dt_y_w));

always @(posedge clk) begin
    if (rst) begin
        s8a_valid <= 0;
    end
    else begin
        s8a_dt_x <= s8a_dt_x_w;
        s8a_dt_y <= s8a_dt_y_w;

        //pass through values
        s8a_valid <= s7b_valid;
        s8a_vx <= s7b_vx;
        s8a_vy <= s7b_vy;
        s8a_step_cnt <= s7b_step_cnt;//increment step count
        s8a_id <= s7b_id;

        s8a_x <= s7b_x;
        s8a_y <= s7b_y;
        s8a_magnet_id <= s7b_magnet_id;

        s8a_settle_count <= s7b_settle_count;
    end
end


//========================================================================================
//                  S8b: update new value of x and y
//========================================================================================

logic signed [W-1:0] out_x_w, out_y_w;

fx_adder_two_input #(.W(W), .F(F)) out_x_adder (.a(s8a_x), .b(s8a_dt_x), .c(out_x_w));
fx_adder_two_input #(.W(W), .F(F)) out_y_adder (.a(s8a_y), .b(s8a_dt_y), .c(out_y_w));

always @(posedge clk) begin
    if (rst) begin
        out_valid <= 0;
    end
    else begin
        //update position with dt_x and dt_y
        out_x <= out_x_w;
        out_y <= out_y_w;

        //pass through values
        out_valid <= s8a_valid;
        out_vx <= s8a_vx;
        out_vy <= s8a_vy;
        out_step_cnt <= s8a_step_cnt+1;//increment step count
        out_id <= s8a_id;
        out_magnet_id <= s8a_magnet_id;

        out_settle_count <= s8a_settle_count;
    end
end



endmodule