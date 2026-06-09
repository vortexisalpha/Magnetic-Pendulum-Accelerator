// Overall 9 lanes
// implementation of one lane,
// each lane corresponds to one horizontal slice of the overall pixel grid

module one_lane_top #(
    parameter W = 18,
    parameter F = 14,
    parameter LUT_SIZE = 8192,
    parameter LUT_ADDR_W = 13,
    parameter Q_WIDTH = 18,
    parameter IMG_W = 360,
    parameter IMG_H = 360,
    parameter LANE_ID   = 0,  // 0..NUM_LANES-1
    parameter NUM_LANES = 9
)(
    input logic clk,
    input logic rst,
    input logic start,
    input logic signed [W-1:0] mag0_x, mag0_y,
    input logic signed [W-1:0] mag1_x, mag1_y,
    input logic signed [W-1:0] mag2_x, mag2_y,

    // resolution inputs
    input logic [$clog2(IMG_W):0] img_w,
    input logic [$clog2(IMG_H):0] img_h, slice_h,
    input logic [$clog2((IMG_H/NUM_LANES)*IMG_W):0]  slice_pixels,

    // active-magnet mask: bit i high => magnet i is active (see lane_main)
    input logic [2:0] mag_active,

    input logic signed [W-1:0] gamma, omega2, h2, mu, dt,

    input logic [W-1:0] r_settle_sq, v_settle,

    input logic [Q_WIDTH-1:0] sum_r_settle_sq_h_sq,

    input logic signed [W-1:0] x_min, y_min, x_step, y_step,

    input logic [1:0]  consec_settle_count,
    input logic [11:0] max_steps,

    // BRAM read port - local address within this lane's frame buffer (0..slice_pixels-1)
    input  logic [$clog2((IMG_H/NUM_LANES)*IMG_W)-1:0] fb_rd_addr,
    output logic [5:0]  fb_rd_data,

    // trajectory input
    input  logic [$clog2(IMG_W*IMG_H)-1:0] traj_px_id,

    // trajectory outputs (only the lane owning the target ever asserts traj_valid)
    output logic traj_valid,
    output logic signed [W-1:0]   traj_x,
    output logic signed [W-1:0]   traj_y,
    output logic traj_done,

    output logic        frame_done
);

    // Strip-partitioning localparams
    localparam SLICE_H_MAX    = IMG_H / NUM_LANES;
    localparam SLICE_PIX_MAX  = SLICE_H_MAX * IMG_W;
    localparam LOCAL_W = $clog2(SLICE_PIX_MAX);

    logic [$clog2(IMG_H)-1:0] q_offset;
    logic [$clog2(IMG_W*IMG_H)-1:0]  pixel_id_base;
    assign q_offset = img_h - 1 - LANE_ID * slice_h;
    assign pixel_id_base = LANE_ID * slice_pixels;

    // scanner
    logic [$clog2(IMG_W)-1:0] scan_p;
    logic [$clog2(IMG_H)-1:0] scan_q;
    logic [$clog2(IMG_W * IMG_H)-1:0] scan_id;
    logic scan_active;

    logic new_px_pending;
    logic new_px_valid;
    logic signed [W-1:0] new_px_x, new_px_y;
    logic [$clog2(IMG_W * IMG_H)-1:0] new_px_id;

    logic coord_mapper_valid_out;
    logic coord_mapper_busy;   // high while a coord occupies the 2-stage mapper pipeline
    always_ff @(posedge clk) begin
        if (rst || !start) begin
            scan_p      <= '0;
            scan_q      <= '0;
            scan_id     <= pixel_id_base; // scan_id initializes at the base value for the horizontal slice, rather than 0
            scan_active <= 1'b0;
        end
        else if (!scan_active && done_count == 0) begin
            scan_active <= 1'b1;
        end
        else if (scan_active && !new_px_pending && !coord_mapper_busy) begin
            if (scan_p == img_w-1) begin
                scan_p <= '0;
                if (scan_q == slice_h-1) begin // row now ends with the border of the slice
                    scan_active <= 1'b0;
                end else begin
                    scan_q <= scan_q + 1;
                end
            end else begin
                scan_p <= scan_p + 1;
            end
            scan_id <= scan_id + 1;
        end
    end

    logic coord_valid_in;
    assign coord_valid_in = scan_active && !new_px_pending && !coord_mapper_busy;

    logic [$clog2(IMG_W * IMG_H)-1:0] coord_id;
    logic signed [W-1:0] x0, y0;
    logic init_step_cnt, init_settle_cnt;

    coordinate_mapper #(.IMG_W(IMG_W), .IMG_H(IMG_H), .W(W), .F(F)) u_coordinate_mapper (
        .clk(clk), .rst(rst),
        .valid_in(coord_valid_in),
        .x_min(x_min), .y_min(y_min),
        .x_step(x_step), .y_step(y_step),
        .p(scan_p),
        .q(q_offset - scan_q),
        .valid_out(coord_mapper_valid_out),
        .busy(coord_mapper_busy),
        .x0(x0), .y0(y0),
        .init_step_cnt(init_step_cnt),
        .init_settle_cnt(init_settle_cnt),
        .pixel_id(scan_id),
        .pixel_id_out(coord_id)
    );

    logic pixel_valid;
    logic signed [W-1:0] pixel_x, pixel_y, pixel_vx, pixel_vy;
    logic [11:0] pixel_step_cnt;
    logic [$clog2(IMG_W * IMG_H)-1:0] pixel_id;
    logic new_px_consume;

    always_ff @(posedge clk) begin
        if (rst || !start) begin
            new_px_pending <= 1'b0;
        end else begin
            if (coord_mapper_valid_out && (!new_px_pending || new_px_consume)) begin
                new_px_pending <= 1'b1;
                new_px_x       <= x0;
                new_px_y       <= y0;
                new_px_id      <= coord_id;
            end else if (new_px_consume) begin
                new_px_pending <= 1'b0;
            end
        end
    end
    assign new_px_valid = new_px_pending;

    logic [1:0] out_magnet_id;
    logic [1:0] settle_count, out_settle_count;
    logic pixel_done;
    logic out_valid;
    logic signed [W-1:0] out_x, out_y, out_vx, out_vy;
    logic [11:0] out_step_cnt;
    logic [$clog2(IMG_W * IMG_H)-1:0] out_id;

    always_comb begin
        pixel_valid    = 1'b0;
        pixel_x        = '0; pixel_y = '0;
        pixel_vx       = '0; pixel_vy = '0;
        pixel_step_cnt = '0;
        pixel_id       = '0;
        settle_count   = '0;
        new_px_consume = 1'b0;

        if (out_valid && !pixel_done) begin
            pixel_valid    = 1'b1;
            pixel_x        = out_x;  pixel_y = out_y;
            pixel_vx       = out_vx; pixel_vy = out_vy;
            pixel_step_cnt = out_step_cnt;
            pixel_id       = out_id;
            settle_count   = out_settle_count;
        end else if (new_px_valid) begin
            pixel_valid    = 1'b1;
            pixel_x        = new_px_x; pixel_y = new_px_y;
            pixel_vx       = '0;       pixel_vy = '0;
            pixel_step_cnt = '0;
            pixel_id       = new_px_id;
            settle_count   = 2'd0;
            new_px_consume = 1'b1;
        end
    end

    lane_main #(.W(W), .F(F), .LUT_SIZE(LUT_SIZE), .LUT_ADDR_W(LUT_ADDR_W), .Q_WIDTH(Q_WIDTH), .IMG_W(IMG_W), .IMG_H(IMG_H)) u_lane_main (
        .clk(clk), .rst(rst),
        .mag0_x(mag0_x), .mag0_y(mag0_y),
        .mag1_x(mag1_x), .mag1_y(mag1_y),
        .mag2_x(mag2_x), .mag2_y(mag2_y),
        .mag_active(mag_active),
        .gamma(gamma), .omega2(omega2), .h2(h2), .mu(mu), .dt(dt),
        .r_settle_sq(r_settle_sq), .v_settle(v_settle),
        .sum_r_settle_sq_h_sq(sum_r_settle_sq_h_sq),
        .pixel_valid(pixel_valid),
        .pixel_x(pixel_x), .pixel_y(pixel_y),
        .pixel_vx(pixel_vx), .pixel_vy(pixel_vy),
        .pixel_step_cnt(pixel_step_cnt),
        .pixel_id(pixel_id),
        .settle_count(settle_count),
        .out_valid(out_valid),
        .out_x(out_x), .out_y(out_y),
        .out_vx(out_vx), .out_vy(out_vy),
        .out_step_cnt(out_step_cnt),
        .out_id(out_id),
        .out_magnet_id(out_magnet_id),
        .out_settle_count(out_settle_count)
    );

    // Trajectory: if output pixel matches the traj id, capture its x, y, and step cnt

    assign traj_valid = out_valid && (out_id == traj_px_id);
    assign traj_x = out_x;
    assign traj_y = out_y;
    assign traj_done  = traj_valid && pixel_done;   // target settled / reached time-out

    logic settled_w, timeout_w;
    detect_settle u_detect (
        .rst(rst), .valid(out_valid),
        .settle_count(out_settle_count), .step_cnt(out_step_cnt),
        .consec_settle_count(consec_settle_count), .max_steps(max_steps),
        .settled(settled_w), .time_out(timeout_w)
    );
    assign pixel_done = settled_w || timeout_w;

    logic [3:0] step_category;
    always_comb begin
        if (timeout_w)             step_category = 4'd0;
        else if (out_step_cnt <= 500)  step_category = 4'd1;
        else if (out_step_cnt <= 1000) step_category = 4'd2;
        else if (out_step_cnt <= 1500) step_category = 4'd3;
        else if (out_step_cnt <= 2000) step_category = 4'd4;
        else if (out_step_cnt <= 2500) step_category = 4'd5;
        else if (out_step_cnt <= 3000) step_category = 4'd6;
        else if (out_step_cnt <= 3500) step_category = 4'd7;
        else if (out_step_cnt <= 4000) step_category = 4'd8;
        else if (out_step_cnt <= 4500) step_category = 4'd9;
        else if (out_step_cnt <= 5000) step_category = 4'd10;
        else                           step_category = 4'd11;
    end

    logic fb_wr_en;
    logic [$clog2(IMG_W * IMG_H)-1:0] fb_wr_offset;
    logic [LOCAL_W-1:0] fb_wr_addr;
    logic [5:0]  fb_wr_data;

    assign fb_wr_en     = out_valid && pixel_done;
    assign fb_wr_offset = out_id - pixel_id_base;
    assign fb_wr_addr   = fb_wr_offset[LOCAL_W-1:0];
    assign fb_wr_data   = {step_category, out_magnet_id};

    frame_buffer #(
        .FB_W  (IMG_W),
        .FB_H  (SLICE_H_MAX), // buffer for each slice
        .DATA_W(6)
    ) u_frame_buffer (
        .clk(clk), .rst(rst),
        .wr_en(fb_wr_en), .wr_addr(fb_wr_addr), .wr_data(fb_wr_data),
        .rd_addr(fb_rd_addr), .rd_data(fb_rd_data)
    );

    logic [LOCAL_W:0] done_count;  // one bit wider so it counts up to slice_pixels
    always_ff @(posedge clk) begin
        if (rst || !start) done_count <= '0;
        else if (fb_wr_en) done_count <= done_count + 1;
    end
    assign frame_done = (done_count >= slice_pixels);

endmodule
