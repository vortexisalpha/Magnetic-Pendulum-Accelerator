// S3: q = dx^2 + dy^2 + h^2
// outputs
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

// new declarations
// also need input declarations of r_settle, v_settle,
logic [1:0] s3_magnet_id；


//settle count declarations
logic [W-1:0] s3_d0, s3_d1, s3_d2;
fx_adder_two_input #(.W(W), .F(F)) s3_d0_adder (.a(s2_dx0_sq), .b(s2_dy0_sq), .c(s3_d0));
fx_adder_two_input #(.W(W), .F(F)) s3_d1_adder (.a(s2_dx1_sq), .b(s2_dy1_sq), .c(s3_d1));
fx_adder_two_input #(.W(W), .F(F)) s3_d2_adder (.a(s2_dx2_sq), .b(s2_dy2_sq), .c(s3_d2));

logic [W-1:0] abs_vx, abs_vy;
logic [1:0] nearest_id;
logic [W-1:0] min_d;


// ties are handled by priority
always_comb begin
    abs_vx = s2_vx[W-1] ? -s2_vx : s2_vx;
    abs_vy = s2_vy[W-1] ? -s2_vy : s2_vy;

    if ((s3_d0 <= s3_d1) && (s3_d0 <= s3_d2)) begin
        nearest_id = 2'd0;
        min_d = s3_d0;
    end 
    else if (s3_d1 <= s3_d2) begin
        nearest_id = 2'd1;
        min_d = s3_d1;
    end 
    else begin
        nearest_id = 2'd2;  
        min_d = s3_d2;
    end
end


always_ff @(posedge clk) begin
    if (rst) begin
        s3_valid <= 0;
        s3_settle_count <= 2'd0;
        s3_magnet_id <= 2'd0;
    end
    
    else begin

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
        
        
        // use r_settle squared
        if (s2_valid) begin 
            if ((min_d < r_settle_sq) && (abs_vx < v_settle) && (abs_vy < v_settle)) begin
                s3_settle_count <= (s2_settle_count != 2'd3) ? s2_settle_count + 2'd1 : 2'd3;
                s3_magnet_id <= nearest_id;
            end
            else begin
                s3_settle_count <= 2'd0;
                s3_magnet_id <= 2'd0;
            end
        end
        else begin
            s3_settle_count <= 2'd0;
            s3_magnet_id <= 2'd0;
        end
    end
end
