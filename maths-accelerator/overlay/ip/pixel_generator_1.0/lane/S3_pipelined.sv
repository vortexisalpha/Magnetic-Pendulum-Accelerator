// S3a: q = dx^2 + dy^2 + h^2
// outputs

localparam Q_WIDTH = 19; // Q 7.12 representation, covers roughly -64 to 63
// written here for now, but best to be declared as a param of the overall lane module

logic signed [Q_WIDTH-1:0] s3a_q0, s3a_q1, s3a_q2;

logic signed [Q_WIDTH-1:0] s3a_q0_w, s3a_q1_w, s3a_q2_w;

logic signed [W-1:0] s3a_dx0, s3a_dy0, s3a_dx1, s3a_dy1, s3a_dx2, s3a_dy2;

logic signed [W-1:0] s3a_x, s3a_y, s3a_vx, s3a_vy;
logic [11:0] s3a_step_cnt;
logic [14:0] s3a_id;
logic s3a_valid;

logic [1:0] s3a_settle_count;

fx_adder_s3 #(.W(Q_WIDTH), .F(F)) s3_q0_adder (.a(s2_dx0_sq), .b(s2_dy0_sq), .c(h2), .d(s3a_q0_w));
fx_adder_s3 #(.W(Q_WIDTH), .F(F)) s3_q1_adder (.a(s2_dx1_sq), .b(s2_dy1_sq), .c(h2), .d(s3a_q1_w));
fx_adder_s3 #(.W(Q_WIDTH), .F(F)) s3_q2_adder (.a(s2_dx2_sq), .b(s2_dy2_sq), .c(h2), .d(s3a_q2_w));

// new declarations
// also need input declarations of r_settle_sq_h2_sum, v_settle,
// input logic signed [Q_WIDTH-1:0] sum_r_settle_sq_h_sq,
// input logic [W-1:0]              v_settle,

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


// S3b: nearest magnet select

logic                       s3b_valid;
logic [1:0]                 s3b_nearest_magnet_id;
logic signed [Q_WIDTH-1:0]  s3b_min_q;

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

// S3c: settle check

logic                       s3c_valid;
logic signed [W-1:0]        s3c_dx0, s3c_dy0, s3c_dx1, s3c_dy1, s3c_dx2, s3c_dy2;
logic signed [W-1:0]        s3c_x, s3c_y, s3c_vx, s3c_vy;
logic [11:0]                s3c_step_cnt;
logic [14:0]                s3c_id;
logic [1:0]                 s3c_nearest_magnet_id;
logic [1:0]                 s3c_settle_count;

logic [Q_WIDTH-1:0]        s3c_q0, s3c_q1, s3c_q2; 

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
