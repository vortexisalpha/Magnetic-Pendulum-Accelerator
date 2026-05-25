module framebuffer_colour_path #(
    parameter FB_W = 160,
    parameter FB_H = 120,
    parameter LABEL_W = 2,
    parameter RGB_W = 8,

    parameter PIXELS = FB_W * FB_H,
    parameter ADDR_W = $clog2(PIXELS)
)(
    input  logic    clk,
    input  logic    rst,

    // Write: from output 
    input  logic    fb_wr_en,
    input  logic [ADDR_W-1:0]    fb_wr_addr,
    input  logic [LABEL_W-1:0]   fb_wr_data,

    // Read: from display
    input  logic [ADDR_W-1:0]   fb_rd_addr,
    input  logic    active_video,

    // RGB output
    output logic [RGB_W-1:0]    red,
    output logic [RGB_W-1:0]    green,
    output logic [RGB_W-1:0]    blue
);

    // Frame buffer output label
    logic [LABEL_W-1:0] fb_rd_label;

    // Delay active_video by one cycle to align with synchronous BRAM read
    logic active_video_d;

    always_ff @(posedge clk)
        if (rst)  active_video_d <= 1'b0;
        else      active_video_d <= active_video;


    frame_buffer #(
        .FB_W(FB_W),
        .FB_H(FB_H),
        .DATA_W(LABEL_W)
    ) frame_buffer_inst (
        .clk(clk),
        .rst(rst),

        .wr_en(fb_wr_en),
        .wr_addr(fb_wr_addr),
        .wr_data(fb_wr_data),

        .rd_addr(fb_rd_addr),
        .rd_data(fb_rd_label)
    );

    colour_encoder #(
        .LABEL_W(LABEL_W),
        .RGB_W(RGB_W)
    ) colour_encoder_inst (
        .label(fb_rd_label),
        .active_video(active_video_d),

        .red(red),
        .green(green),
        .blue(blue)
    );

endmodule