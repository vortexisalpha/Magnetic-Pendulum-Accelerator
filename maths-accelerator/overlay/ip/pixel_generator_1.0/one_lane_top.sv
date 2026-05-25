module one_lane_top #(
    parameter W = 16,
    parameter F = 12,
    parameter LUT_SIZE = 1024, //1024 entries
    parameter LUT_ADDR_W = 10, // 2^10 = 1024
    parameter Q_WIDTH = 18, // Q 7.12 representation, covers roughly -64 to 63
    parameter IMG_W = 160,
    parameter IMG_H = 120
)(
    input logic clk,
    input logic rst,
    input logic signed [W-1:0] mag0_x, mag0_y,
    input logic signed [W-1:0] mag1_x, mag1_y,
    input logic signed [W-1:0] mag2_x, mag2_y,

    input logic signed [W-1:0] gamma, omega2, h2, mu, dt,   //might change to unsigned if we decide to only support positive values for these parameters

    //not signed as it's always positive
    input logic [W-1:0] r_settle_sq, v_settle,

    
    input logic [Q_WIDTH-1:0] sum_r_settle_sq_h_sq, // might make it unsigned

    input logic signed [W-1:0] x_min, y_min, x_step, y_step,

    input logic [1:0]  consec_settle_count,
    input logic [11:0] max_steps, // might change to 11 bits as 2000 time steps is usually sufficient

    // display stuff
    input  logic [14:0]          fb_rd_addr,
    input  logic                 active_video,
    output logic [7:0]           red, green, blue
);


    // scanner that iterates over the pixel indexes and drives the coord mapper
    logic [$clog2(IMG_W)-1:0] scan_p;
    logic [$clog2(IMG_H)-1:0] scan_q;
    logic [$clog2(IMG_W * IMG_H)-1:0]    scan_id;       // flat pixel counter, avoids q*IMG_W multiply
    logic           scan_active;   // still pixels left to emit this frame

    logic           new_px_pending;
    logic           new_px_valid;
    logic signed [W-1:0] new_px_x, new_px_y;
    logic [$clog2(IMG_W * IMG_H)-1:0]    new_px_id;

    logic coord_mapper_valid_out;
    always_ff @(posedge clk) begin
        if (rst) begin
            scan_p      <= '0;
            scan_q      <= '0;
            scan_id     <= '0;
            scan_active <= 1'b1;
        end 
        else if (scan_active && !new_px_pending && !coord_mapper_valid_out) begin
            // advance only when havent went over all the pixels and coord mapper input slot is free
            if (scan_p == IMG_W-1) begin
                scan_p <= '0; // once reaches end of a row, start with col 0 on next row
                if (scan_q == IMG_H-1) begin
                    scan_active <= 1'b0; // all pixels emitted
                end 
                else begin
                    scan_q <= scan_q + 1; // goes to next row
                end
            end 
            else begin
                scan_p <= scan_p + 1; // goes to next col
            end
            scan_id <= scan_id + 1; // increments pixel id
        end
    end

    // scanner feeds coord_mapper

    logic coord_valid_in;
    assign coord_valid_in = scan_active && !new_px_pending && !coord_mapper_valid_out; // only feed in new pixel when previous pixel has been consumed by coord mapper, and coord mapper is ready to accept new pixel (valid_out is low)

    // pixel id is passed thru
    logic [$clog2(IMG_W * IMG_H)-1:0] coord_id;

    logic signed [W-1:0] x0;
    logic signed [W-1:0] y0;
    logic init_step_cnt, init_settle_cnt;

    coordinate_mapper #(.IMG_W(IMG_W), .IMG_H(IMG_H), .W(W), .F(F)) u_coordinate_mapper (
        .clk(clk),
        .rst(rst),

        .valid_in(coord_valid_in),

        .x_min(x_min),
        .y_min(y_min),
        .x_step(x_step),
        .y_step(y_step),

        .p(scan_p),
        .q(scan_q),

        .valid_out(coord_mapper_valid_out),
        .x0(x0),
        .y0(y0),

        .init_step_cnt(init_step_cnt),
        .init_settle_cnt(init_settle_cnt),
        // these two are not needed as the arbiter sets these to 0 by default already

        .pixel_id(scan_id),
        .pixel_id_out(coord_id)
    );

    // new_px_reg
    // captures coord_mapper output and holds until arbiter consumes it
    // stalls input when an unsettled pixel is passed back from the lane output


    logic pixel_valid;
    logic signed [W-1:0] pixel_x, pixel_y, pixel_vx, pixel_vy;
    logic [11:0] pixel_step_cnt;
    logic [14:0] pixel_id;
    logic new_px_consume;

    always_ff @(posedge clk) begin
        if (rst) begin
            new_px_pending <= 1'b0;
        end 
        else begin
            if (coord_mapper_valid_out && (!new_px_pending || new_px_consume)) begin
                // the OR deals with situation where the reg is about to be freed i.e. new_px_pending goes low next cycle,
                // as new_px+pending is set to 0 on the cycle after new_px_consume
                // new_px_consume shows that arbiter has dealt with previous px
                new_px_pending <= 1'b1;
                new_px_x       <= x0;
                new_px_y       <= y0;
                new_px_id      <= coord_id;
            end 
            else if (new_px_consume) begin
                new_px_pending <= 1'b0; // reg is waiting for new pixel once previous pixel has been consumed by the arbiter
            end
        end
    end
    assign new_px_valid = new_px_pending;

    logic [1:0] out_magnet_id;
    logic [1:0] settle_count, out_settle_count;
    
    
    // fsm output
    logic pixel_done;

    // lane outputs
    logic out_valid;

    logic signed [W-1:0] out_x, out_y, out_vx, out_vy;
    logic [11:0] out_step_cnt;
    logic [14:0] out_id;


    // input arbiter logic
    always_comb begin
        // defaults
        pixel_valid    = 1'b0;
        pixel_x        = '0;
        pixel_y        = '0;
        pixel_vx       = '0;
        pixel_vy       = '0;
        pixel_step_cnt = '0;
        pixel_id       = '0;
        settle_count   = '0;
        new_px_consume = 1'b0;

        if (out_valid && !pixel_done) begin
            // unsettled / non-timed-out pixel feeds directly back
            // priority over inputting a new pixel
            // initially, when lane is still filling up, out_valid would be low, so would still pass in new pixels

            pixel_valid    = 1'b1; // pixel gets passed back in
            pixel_x        = out_x;
            pixel_y        = out_y;
            pixel_vx       = out_vx;
            pixel_vy       = out_vy;
            pixel_step_cnt = out_step_cnt;
            pixel_id       = out_id;
            settle_count   = out_settle_count;

        end 
        else if (new_px_valid) begin
            // slot is free (pipeline drained or pixel just finished),
            // inject a fresh pixel with zero velocity
            pixel_valid    = 1'b1; // passes in new pixel if one pixel in the lane is done
            pixel_x        = new_px_x;
            pixel_y        = new_px_y;
            pixel_vx       = '0;
            pixel_vy       = '0;
            pixel_step_cnt = '0;
            pixel_id       = new_px_id;
            settle_count   = 2'd0;
            new_px_consume = 1'b1; // arbiter tells the new_px_reg that it has conumsed / taken in the new pixel
        end
        // else: bubble, pixel_valid stays 0
    end

    lane_main #(.W(W), .F(F), .LUT_SIZE(LUT_SIZE), .LUT_ADDR_W (LUT_ADDR_W), .Q_WIDTH(Q_WIDTH)) u_lane_main (
        .clk(clk),
        .rst(rst),

        // Physical parameters
        .mag0_x(mag0_x),
        .mag0_y(mag0_y),
        .mag1_x(mag1_x),
        .mag1_y(mag1_y),
        .mag2_x(mag2_x),
        .mag2_y(mag2_y),

        .gamma(gamma),
        .omega2(omega2),
        .h2(h2),
        .mu(mu),
        .dt(dt),

        .r_settle_sq(r_settle_sq),
        .v_settle(v_settle),

        .sum_r_settle_sq_h_sq(sum_r_settle_sq_h_sq),

        // Pixel inputs
        .pixel_valid(pixel_valid),
        .pixel_x(pixel_x),
        .pixel_y(pixel_y),
        .pixel_vx(pixel_vx),
        .pixel_vy(pixel_vy),
        .pixel_step_cnt(pixel_step_cnt),
        .pixel_id(pixel_id),

        .settle_count(settle_count),

        // Outputs
        .out_valid(out_valid),
        .out_x(out_x),
        .out_y(out_y),
        .out_vx(out_vx),
        .out_vy(out_vy),
        .out_step_cnt(out_step_cnt),
        .out_id(out_id),
        .out_magnet_id(out_magnet_id),
        .out_settle_count(out_settle_count)
);


    // feedback logic
    logic settled_w, timeout_w;

    fsm_settle u_fsm (
        .rst                 (rst),
        .valid               (out_valid),
        .settle_count        (out_settle_count),
        .step_cnt            (out_step_cnt),
        .consec_settle_count (consec_settle_count),
        .max_steps           (max_steps),
        .settled             (settled_w),
        .time_out            (timeout_w)
    );

    // pixel_done is the key signal everything else uses
    assign pixel_done = settled_w || timeout_w;



    // frame buffer


    // encode result into 2 bits
    logic [1:0] result_data;
    assign result_data = out_magnet_id; // even if time_out, output nearest magnet id

    logic fb_wr_en;
    logic [14:0] fb_wr_addr;

    // connect to frame_buffer write port
    assign fb_wr_en   = out_valid && pixel_done;
    assign fb_wr_addr = out_id;          // pixel_id is flat index, matches ADDR_W

    framebuffer_colour_path #(
        .FB_W(IMG_W),
        .FB_H(IMG_H)
    ) u_fb_colour (
        .clk         (clk),
        .rst         (rst),
        // write side — directly from lane outputs
        .fb_wr_en    (fb_wr_en),
        .fb_wr_addr  (fb_wr_addr),
        .fb_wr_data  (result_data),
        // read side — from your display timing controller
        .fb_rd_addr  (fb_rd_addr),
        .active_video(active_video),
        .red         (red),
        .green       (green),
        .blue        (blue)
    );

endmodule