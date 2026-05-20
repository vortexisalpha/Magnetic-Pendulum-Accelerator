

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


    //========================================================================================
    //                  outputs
    //========================================================================================
    output logic out_valid,
    output logic signed [W-1:0] out_x, out_y, out_vx, out_vy,
    output logic [11:0] out_step_cnt,
    output logic [14:0] out_id
);

//========================================================================================
//                  S1: compute dx and dy (0 dsps)
//========================================================================================

//outputs:
logic signed [W-1:0] s1_dx0, s1_dy0, s1_dx1, s1_dy1, s1_dx2, s1_dy2;
logic signed [W-1:0] s1_x, s1_y, s1_vx, s1_vy;
logic [11:0] s1_step_cnt;
logic [14:0] s1_id;
logic s1_valid;

always @(posedge clk) begin
    if (rst) begin
        s1_valid <= 0;
    end
    else begin
        s1_valid <= pixel_valid;
        s1_dx0 <= mag0_x - pixel_x;
        s1_dy0 <= mag0_y - pixel_y;
        s1_dx1 <= mag1_x - pixel_x;
        s1_dy1 <= mag1_y - pixel_y;
        s1_dx2 <= mag2_x - pixel_x;
        s1_dy2 <= mag2_y - pixel_y;
        s1_x <= pixel_x;
        s1_y <= pixel_y;
        s1_vx <= pixel_vx;
        s1_vy <= pixel_vy;
        s1_step_cnt <= pixel_step_cnt;
        s1_id <= pixel_id;
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

//square dx and dy values per magnet (6 total) using fx_mul module - 6 DSPs used
fx_mul #(.W(W), .F(F)) s2_m_dx0 (.a(s1_dx0),.b(s1_dx0),.c(s2_dx0_sq));
fx_mul #(.W(W), .F(F)) s2_m_dy0 (.a(s1_dy0),.b(s1_dy0),.c(s2_dy0_sq));
fx_mul #(.W(W), .F(F)) s2_m_dx1 (.a(s1_dx1),.b(s1_dx1),.c(s2_dx1_sq));
fx_mul #(.W(W), .F(F)) s2_m_dy1 (.a(s1_dy1),.b(s1_dy1),.c(s2_dy1_sq));
fx_mul #(.W(W), .F(F)) s2_m_dx2 (.a(s1_dx2),.b(s1_dx2),.c(s2_dx2_sq));
fx_mul #(.W(W), .F(F)) s2_m_dy2 (.a(s1_dy2),.b(s1_dy2),.c(s2_dy2_sq));

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
    end
end

//========================================================================================
//                  S3: q = dx^2 + dy^2 + h^2
//========================================================================================
//outputs
logic signed [W-1:0] s3_q0, s3_q1, s3_q2;
logic signed [W-1:0] s3_dx0, s3_dy0, s3_dx1, s3_dy1, s3_dx2, s3_dy2;
logic signed [W-1:0] s3_x, s3_y, s3_vx, s3_vy;
logic [11:0] s3_step_cnt;
logic [14:0] s3_id;
logic s3_valid;

always @(posedge clk) begin
    if (rst) begin
        s3_valid <= 0;
    end
    else begin

        //worried about potential overflow here - we would need to investigate if it actually saturates by looking at values
        s3_q0 <= s2_dx0_sq + s2_dy0_sq + h2;
        s3_q1 <= s2_dx1_sq + s2_dy1_sq + h2;
        s3_q2 <= s2_dx2_sq + s2_dy2_sq + h2;

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

//dx, dy values multipled with qinv - 6 DSPs used
fx_mul #(.W(W), .F(F)) s5_m_dx0 (.a(s4_dx0),.b(s4_invq0),.c(s5_dx_invq0));
fx_mul #(.W(W), .F(F)) s5_m_dy0 (.a(s4_dy0),.b(s4_invq0),.c(s5_dy_invq0));
fx_mul #(.W(W), .F(F)) s5_m_dx1 (.a(s4_dx1),.b(s4_invq1),.c(s5_dx_invq1));
fx_mul #(.W(W), .F(F)) s5_m_dy1 (.a(s4_dy1),.b(s4_invq1),.c(s5_dy_invq1));
fx_mul #(.W(W), .F(F)) s5_m_dx2 (.a(s4_dx2),.b(s4_invq2),.c(s5_dx_invq2));
fx_mul #(.W(W), .F(F)) s5_m_dy2 (.a(s4_dy2),.b(s4_invq2),.c(s5_dy_invq2));

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
    end
end

//========================================================================================
//                  S6: multiply dx, dy with qinv (6 DSPs)
//========================================================================================
//outputs

logic signed [W-1:0] s6_ax;
logic signed [W-1:0] s6_ay;
logic signed [W-1:0] s6_x, s6_y, s6_vx, s6_vy;
logic [11:0] s6_step_cnt;
logic [14:0] s6_id;
logic s6_valid;

//intermediate
logic signed [W-1:0] dx_invq_sum, dy_invq_sum;

// intermediate wires for multiplier outputs
logic signed [W-1:0] gamma_vel_x_w, gamma_vel_y_w;
logic signed [W-1:0] omega2_pos_x_w, omega2_pos_y_w;
logic signed [W-1:0] mu_dx_invq_w, mu_dy_invq_w;

//multiply w physical paramaters
fx_mul #(.W(W), .F(F)) m_gamma_vel_x (.a(gamma), .b(s5_vx), .c(gamma_vel_x_w));
fx_mul #(.W(W), .F(F)) m_omega2_pos_x (.a(omega2), .b(s5_x), .c(omega2_pos_x_w));
fx_mul #(.W(W), .F(F)) m_gamma_vel_y (.a(gamma), .b(s5_vy), .c(gamma_vel_y_w));
fx_mul #(.W(W), .F(F)) m_omega2_pos_y (.a(omega2), .b(s5_y), .c(omega2_pos_y_w));
fx_mul #(.W(W), .F(F)) m_mu_dx_invq (.a(mu), .b(dx_invq_sum), .c(mu_dx_invq_w));
fx_mul #(.W(W), .F(F)) m_mu_dy_invq (.a(mu), .b(dy_invq_sum), .c(mu_dy_invq_w));

always_comb begin
    // sum contributions from all magnets
    dx_invq_sum = s5_dx_invq0 + s5_dx_invq1 + s5_dx_invq2;
    dy_invq_sum = s5_dy_invq0 + s5_dy_invq1 + s5_dy_invq2;

    // combine multiplier outputs into accelerations
    s6_ax = mu_dx_invq_w - gamma_vel_x_w + omega2_pos_x_w;
    s6_ay = mu_dy_invq_w - gamma_vel_y_w + omega2_pos_y_w;
end

always @(posedge clk) begin
    if (rst) begin
        s6_valid <= 0;
    end
    else begin
        //pass through values
        s6_valid <= s5_valid;
        s6_x <= s5_x;
        s6_y <= s5_y;
        s6_vx <= s5_vx;
        s6_vy <= s5_vy;
        s6_step_cnt <= s5_step_cnt;
        s6_id <= s5_id;
    end
end


//========================================================================================
//                  S7: update new value of v
//========================================================================================
//outputs

logic signed [W-1:0] s7_x, s7_y, s7_vx, s7_vy;
logic [11:0] s7_step_cnt;
logic [14:0] s7_id;
logic s7_valid;

//intermediate
logic signed [W-1:0] dt_ax, dt_ay;

fx_mul #(.W(W), .F(F)) m_dt_ax (.a(dt),.b(s6_ax),.c(dt_ax));
fx_mul #(.W(W), .F(F)) m_dt_ay (.a(dt),.b(s6_ay),.c(dt_ay));

always @(posedge clk) begin
    if (rst) begin
        s7_valid <= 0;
    end
    else begin
        //update velocity with dt_ax and dt_ay
        s7_vx <= s6_vx + dt_ax;
        s7_vy <= s6_vy + dt_ay;

        //pass through values
        s7_valid <= s6_valid;
        s7_x <= s6_x;
        s7_y <= s6_y;
        s7_step_cnt <= s6_step_cnt;
        s7_id <= s6_id;
    end
end

//========================================================================================
//                  S8: update new value of x and y
//========================================================================================
//outputs

//intermediate
logic signed [W-1:0] dt_x, dt_y;

fx_mul #(.W(W), .F(F)) m_dt_x (.a(dt),.b(s7_vx),.c(dt_x));
fx_mul #(.W(W), .F(F)) m_dt_y (.a(dt),.b(s7_vy),.c(dt_y));

always @(posedge clk) begin
    if (rst) begin
        out_valid <= 0;
    end
    else begin
        //update position with dt_x and dt_y
        out_x <= s7_x + dt_x;
        out_y <= s7_y + dt_y;

        //pass through values
        out_valid <= s7_valid;
        out_vx <= s7_vx;
        out_vy <= s7_vy;
        out_step_cnt <= s7_step_cnt+1;//increment step count
        out_id <= s7_id;
    end
end

endmodule