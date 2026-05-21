module lane_main #(
    parameter W = 16,
    parameter F = 12,
    parameter LUT_SIZE = 1024, //1024 entries
    parameter LUT_ADDR_W = 10 // 2^10 = 1024
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
//                  S3: q = dx^2 + dy^2 + h^2
//========================================================================================
//outputs
logic signed [W-1:0] s3_q0, s3_q1, s3_q2;

logic signed [W-1:0] s3_q0_w, s3_q1_w, s3_q2_w;

logic signed [W-1:0] s3_dx0, s3_dy0, s3_dx1, s3_dy1, s3_dx2, s3_dy2;
logic signed [W-1:0] s3_x, s3_y, s3_vx, s3_vy;
logic [11:0] s3_step_cnt;
logic [14:0] s3_id;
logic s3_valid;

logic [1:0] s3_settle_count;

fx_adder_s3 #(.W(W), .F(F)) s3_q0_adder (.a(s2_dx0_sq), .b(s2_dy0_sq), .c(h2), .d(s3_q0_w));
fx_adder_s3 #(.W(W), .F(F)) s3_q1_adder (.a(s2_dx1_sq), .b(s2_dy1_sq), .c(h2), .d(s3_q1_w));
fx_adder_s3 #(.W(W), .F(F)) s3_q2_adder (.a(s2_dx2_sq), .b(s2_dy2_sq), .c(h2), .d(s3_q2_w));

always @(posedge clk) begin
    if (rst) begin
        s3_valid <= 0;
    end
    else begin

        //worried about potential overflow here - we would need to investigate if it actually saturates by looking at values
        //use q value as Q6.12. and truncate it on LUT address index

        //UPDATE: added saturation to prevent overflow - don't think we need to make q wider
        //TODO - we could potentially make q wider and not saturate?
        s3_q0 <= s3_q0_w;
        s3_q1 <= s3_q1_w;
        s3_q2 <= s3_q2_w;

        //pass through values
        s3_valid <= s2_valid;
        s3_dx0 <= s2_dx0;
        s3_dy0 <= s2_dy0;
        s3_dx1 <= s2_dx1;
        s3_dy1 <= s2_dy1;
        s3_dx2 <= s2_dx2;
        s3_dy2 <= s2_dy2;
        s3_x <= s2_x;
        s3_y <= s2_y;
        s3_vx <= s2_vx;
        s3_vy <= s2_vy;
        s3_step_cnt <= s2_step_cnt;
        s3_id <= s2_id;
        s3_settle_count <= s2_settle_count;
    end
end


//========================================================================================
//                  S4: q^-3/2 (invq) calcution via LUT (0 DSP)
//========================================================================================

//outputs
logic signed [W-1:0] s4_invq0, s4_invq1, s4_invq2;
logic signed [W-1:0] s4_dx0, s4_dy0, s4_dx1, s4_dy1, s4_dx2, s4_dy2;
logic signed [W-1:0] s4_x, s4_y, s4_vx, s4_vy;
logic [11:0] s4_step_cnt;
logic [14:0] s4_id;
logic s4_valid;

logic [1:0] s4_settle_count;

lut_bram #(.W(W), .F(F), .LUT_SIZE(LUT_SIZE), .LUT_ADDR_W(LUT_ADDR_W)) lut0 (
    .clk(clk),
    .rst(rst),
    .addr(s3_q0),
    .data_out(s4_invq0)
);

lut_bram #(.W(W), .F(F), .LUT_SIZE(LUT_SIZE), .LUT_ADDR_W(LUT_ADDR_W)) lut1 (
    .clk(clk),
    .rst(rst),
    .addr(s3_q1),
    .data_out(s4_invq1)
);

lut_bram #(.W(W), .F(F), .LUT_SIZE(LUT_SIZE), .LUT_ADDR_W(LUT_ADDR_W)) lut2 (
    .clk(clk),
    .rst(rst),
    .addr(s3_q2),
    .data_out(s4_invq2)
);

always @(posedge clk) begin
    if (rst) begin
        s4_valid <= 0;
    end
    else begin
        //pass through values
        s4_valid <= s3_valid;
        s4_dx0 <= s3_dx0;
        s4_dy0 <= s3_dy0;
        s4_dx1 <= s3_dx1;
        s4_dy1 <= s3_dy1;
        s4_dx2 <= s3_dx2;
        s4_dy2 <= s3_dy2;
        s4_x <= s3_x;
        s4_y <= s3_y;
        s4_vx <= s3_vx;
        s4_vy <= s3_vy;
        s4_step_cnt <= s3_step_cnt;
        s4_id <= s3_id;
        s4_settle_count <= s3_settle_count;
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

logic [1:0] s5_settle_count;

//intermediate combinatorial outputs for fx_mul modules
logic signed [W-1:0] s5_dx_invq0_w, s5_dx_invq1_w, s5_dx_invq2_w;
logic signed [W-1:0] s5_dy_invq0_w, s5_dy_invq1_w, s5_dy_invq2_w;

//dx, dy values multipled with qinv - 6 DSPs used
fx_mul #(.W(W), .F(F)) s5_m_dx0 (.a(s4_dx0),.b(s4_invq0),.c(s5_dx_invq0_w));
fx_mul #(.W(W), .F(F)) s5_m_dy0 (.a(s4_dy0),.b(s4_invq0),.c(s5_dy_invq0_w));
fx_mul #(.W(W), .F(F)) s5_m_dx1 (.a(s4_dx1),.b(s4_invq1),.c(s5_dx_invq1_w));
fx_mul #(.W(W), .F(F)) s5_m_dy1 (.a(s4_dy1),.b(s4_invq1),.c(s5_dy_invq1_w));
fx_mul #(.W(W), .F(F)) s5_m_dx2 (.a(s4_dx2),.b(s4_invq2),.c(s5_dx_invq2_w));
fx_mul #(.W(W), .F(F)) s5_m_dy2 (.a(s4_dy2),.b(s4_invq2),.c(s5_dy_invq2_w));

always @(posedge clk) begin
    if (rst) begin
        s5_valid <= 0;
    end
    else begin
        //pass through values
        s5_valid <= s4_valid;
        s5_x <= s4_x;
        s5_y <= s4_y;
        s5_vx <= s4_vx;
        s5_vy <= s4_vy;
        s5_step_cnt <= s4_step_cnt;
        s5_id <= s4_id;
        // register invq products so they align w pipeline
        s5_dx_invq0 <= s5_dx_invq0_w;
        s5_dy_invq0 <= s5_dy_invq0_w;
        s5_dx_invq1 <= s5_dx_invq1_w;
        s5_dy_invq1 <= s5_dy_invq1_w;
        s5_dx_invq2 <= s5_dx_invq2_w;
        s5_dy_invq2 <= s5_dy_invq2_w;
        s5_settle_count <= s4_settle_count;
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

fx_adder_s3 #(.W(W), .F(F)) dx_invq_adder (.a(s5_dx_invq0), .b(s5_dx_invq1), .c(s5_dx_invq2), .d(dx_invq_sum_w));
fx_adder_s3 #(.W(W), .F(F)) dy_invq_adder (.a(s5_dy_invq0), .b(s5_dy_invq1), .c(s5_dy_invq2), .d(dy_invq_sum_w));

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
//            S6b: multiply gamma_vel, omega2x, sum all dy dx with qinv multiplied
//========================================================================================
//outputs
logic signed [W-1:0] s6b_x, s6b_y, s6b_vx, s6b_vy;
logic [11:0] s6b_step_cnt;
logic [14:0] s6b_id;
logic s6b_valid;

logic signed [W-1:0] s6b_ax, s6b_ay;


logic [1:0] s6b_settle_count;

logic signed [W-1:0] s6b_ax_w;
logic signed [W-1:0] s6b_ay_w;
logic signed [W-1:0] mu_dx_invq_w, mu_dy_invq_w;


fx_mul #(.W(W), .F(F)) m_mu_dx_invq (.a(mu), .b(s6a_dx_invq_sum), .c(mu_dx_invq_w));
fx_mul #(.W(W), .F(F)) m_mu_dy_invq (.a(mu), .b(s6a_dy_invq_sum), .c(mu_dy_invq_w));

fx_sub_three_input #(.W(W), .F(F)) s6b_ax_subtractor (.a(mu_dx_invq_w), .b(s6a_gamma_vel_x), .c(s6a_omega2_pos_x), .d(s6b_ax_w));
fx_sub_three_input #(.W(W), .F(F)) s6b_ay_subtractor (.a(mu_dy_invq_w), .b(s6a_gamma_vel_y), .c(s6a_omega2_pos_y), .d(s6b_ay_w));


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

        //register ax and ay for pipeline alignment
        s6b_ax <= s6b_ax_w;
        s6b_ay <= s6b_ay_w;
    end
end


//========================================================================================
//                  S7: update new value of v
//========================================================================================
//outputs

logic signed [W-1:0] s7_x, s7_y, s7_vx, s7_vy;
logic signed [W-1:0] s7_vx_w, s7_vy_w;
logic [11:0] s7_step_cnt;
logic [14:0] s7_id;
logic s7_valid;

logic [1:0] s7_settle_count;

//intermediate
logic signed [W-1:0] dt_ax, dt_ay;

fx_mul #(.W(W), .F(F)) m_dt_ax (.a(dt),.b(s6b_ax),.c(dt_ax));
fx_mul #(.W(W), .F(F)) m_dt_ay (.a(dt),.b(s6b_ay),.c(dt_ay));

fx_adder_two_input #(.W(W), .F(F)) s7_vx_adder (.a(s6b_vx), .b(dt_ax), .c(s7_vx_w));
fx_adder_two_input #(.W(W), .F(F)) s7_vy_adder (.a(s6b_vy), .b(dt_ay), .c(s7_vy_w));

always @(posedge clk) begin
    if (rst) begin
        s7_valid <= 0;
    end
    else begin
        //update velocity with dt_ax and dt_ay
        s7_vx <= s7_vx_w;
        s7_vy <= s7_vy_w;

        //pass through values
        s7_valid <= s6b_valid;
        s7_x <= s6b_x;
        s7_y <= s6b_y;
        s7_step_cnt <= s6b_step_cnt;
        s7_id <= s6b_id;

        s7_settle_count <= s6b_settle_count;
    end
end

//========================================================================================
//                  S8: update new value of x and y
//========================================================================================
//outputs

//intermediate
logic signed [W-1:0] dt_x, dt_y;

logic signed [W-1:0] out_x_w, out_y_w;

fx_mul #(.W(W), .F(F)) m_dt_x (.a(dt),.b(s7_vx),.c(dt_x));
fx_mul #(.W(W), .F(F)) m_dt_y (.a(dt),.b(s7_vy),.c(dt_y));

fx_adder_two_input #(.W(W), .F(F)) out_x_adder (.a(s7_x), .b(dt_x), .c(out_x_w));
fx_adder_two_input #(.W(W), .F(F)) out_y_adder (.a(s7_y), .b(dt_y), .c(out_y_w));

always @(posedge clk) begin
    if (rst) begin
        out_valid <= 0;
    end
    else begin
        //update position with dt_x and dt_y
        out_x <= out_x_w;
        out_y <= out_y_w;

        //pass through values
        out_valid <= s7_valid;
        out_vx <= s7_vx;
        out_vy <= s7_vy;
        out_step_cnt <= s7_step_cnt+1;//increment step count
        out_id <= s7_id;

        out_settle_count <= s7_settle_count;
    end
end

endmodule