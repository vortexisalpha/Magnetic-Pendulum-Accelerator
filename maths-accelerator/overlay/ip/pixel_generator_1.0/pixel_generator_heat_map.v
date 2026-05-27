// pixel_generator.v — Changes from original (HDMI display version):
//
// REMOVED:
//   - packer module and r/g/b inputs
//   - x/y raster counters (X_SIZE, Y_SIZE, lastx, lasty, first)
//   - px/py sub-pixel counters and fb_rd_addr display calculation
//   - active_video, red, green, blue connections to one_lane_top
//   - regfile renamed to reg_file
//
// ADDED:
//   - Result readout state machine (PS_IDLE/ISSUE/COLLECT/SEND/DONE)
//   - Triggered by frame_done from one_lane_top
//   - Reads BRAM sequentially (pixel 0..19199) after computation completes
//   - Packs 4 results per 32-bit AXI-Stream word (1 byte per pixel)
//   - Each byte: [5:2]=step_category, [1:0]=magnet_id, 6 bits in total
//   - 4800 words total, tlast on last word (signals end of stream), tuser=SOF on first word (start of frame)
//   - AXI-Stream driven directly (no packer needed)
//
// one_lane_top connections updated:
//   - fb_rd_addr: driven by readout state machine
//   - fb_rd_data: 6-bit result per pixel
//   - frame_done: triggers readout

module pixel_generator(
    input           out_stream_aclk,
    input           s_axi_lite_aclk,
    input           axi_resetn,
    input           periph_resetn,

    // AXI-Stream output
    output [31:0]   out_stream_tdata,
    output [3:0]    out_stream_tkeep,
    output          out_stream_tlast,
    input           out_stream_tready,
    output          out_stream_tvalid,
    output [0:0]    out_stream_tuser,

    // AXI-Lite slave
    input  [AXI_LITE_ADDR_WIDTH-1:0] s_axi_lite_araddr,
    output          s_axi_lite_arready,
    input           s_axi_lite_arvalid,
    input  [AXI_LITE_ADDR_WIDTH-1:0] s_axi_lite_awaddr,
    output          s_axi_lite_awready,
    input           s_axi_lite_awvalid,
    input           s_axi_lite_bready,
    output [1:0]    s_axi_lite_bresp,
    output          s_axi_lite_bvalid,
    output [31:0]   s_axi_lite_rdata,
    input           s_axi_lite_rready,
    output [1:0]    s_axi_lite_rresp,
    output          s_axi_lite_rvalid,
    input  [31:0]   s_axi_lite_wdata,
    output          s_axi_lite_wready,
    input           s_axi_lite_wvalid
);

parameter  REG_FILE_SIZE       = 32;
localparam REG_FILE_AWIDTH     = $clog2(REG_FILE_SIZE);
parameter  AXI_LITE_ADDR_WIDTH = 8;

// AXI-Lite state encodings
localparam AWAIT_WADD_AND_DATA = 3'b000;
localparam AWAIT_WDATA         = 3'b001;
localparam AWAIT_WADD          = 3'b010;
localparam AWAIT_WRITE         = 3'b100;
localparam AWAIT_RESP          = 3'b101;
localparam AWAIT_RADD          = 2'b00;
localparam AWAIT_FETCH         = 2'b01;
localparam AWAIT_READ          = 2'b10;
localparam AXI_OK              = 2'b00;
localparam AXI_ERR             = 2'b10;

reg [31:0]                reg_file [REG_FILE_SIZE-1:0];
reg [REG_FILE_AWIDTH-1:0] writeAddr, readAddr;
reg [31:0]                readData, writeData;
reg [1:0]                 readState  = AWAIT_RADD;
reg [2:0]                 writeState = AWAIT_WADD_AND_DATA;

// -------------------------------------------------------------------------
// AXI-Lite read
// -------------------------------------------------------------------------
always @(posedge s_axi_lite_aclk) begin
    readData <= reg_file[readAddr];
    if (!axi_resetn) begin
        readState <= AWAIT_RADD;
    end else case (readState)
        AWAIT_RADD:  if (s_axi_lite_arvalid) begin
                         readAddr  <= s_axi_lite_araddr[2+:REG_FILE_AWIDTH];
                         readState <= AWAIT_FETCH;
                     end
        AWAIT_FETCH: readState <= AWAIT_READ;
        AWAIT_READ:  if (s_axi_lite_rready) readState <= AWAIT_RADD;
        default:     readState <= AWAIT_RADD;
    endcase
end

assign s_axi_lite_arready = (readState == AWAIT_RADD);
assign s_axi_lite_rresp   = (readAddr < REG_FILE_SIZE) ? AXI_OK : AXI_ERR;
assign s_axi_lite_rvalid  = (readState == AWAIT_READ);
assign s_axi_lite_rdata   = readData;

// -------------------------------------------------------------------------
// AXI-Lite write
// -------------------------------------------------------------------------
always @(posedge s_axi_lite_aclk) begin
    if (!axi_resetn) begin
        writeState <= AWAIT_WADD_AND_DATA;
    end else case (writeState)
        AWAIT_WADD_AND_DATA: begin
            case ({s_axi_lite_awvalid, s_axi_lite_wvalid})
                2'b10: begin
                    writeAddr  <= s_axi_lite_awaddr[2+:REG_FILE_AWIDTH];
                    writeState <= AWAIT_WDATA;
                end
                2'b01: begin
                    writeData  <= s_axi_lite_wdata;
                    writeState <= AWAIT_WADD;
                end
                2'b11: begin
                    writeData  <= s_axi_lite_wdata;
                    writeAddr  <= s_axi_lite_awaddr[2+:REG_FILE_AWIDTH];
                    writeState <= AWAIT_WRITE;
                end
                default: writeState <= AWAIT_WADD_AND_DATA;
            endcase
        end
        AWAIT_WDATA: if (s_axi_lite_wvalid) begin
                         writeData  <= s_axi_lite_wdata;
                         writeState <= AWAIT_WRITE;
                     end
        AWAIT_WADD:  if (s_axi_lite_awvalid) begin
                         writeAddr  <= s_axi_lite_awaddr[2+:REG_FILE_AWIDTH];
                         writeState <= AWAIT_WRITE;
                     end
        AWAIT_WRITE: begin
                         reg_file[writeAddr] <= writeData;
                         writeState <= AWAIT_RESP;
                     end
        AWAIT_RESP:  if (s_axi_lite_bready) writeState <= AWAIT_WADD_AND_DATA;
        default:     writeState <= AWAIT_WADD_AND_DATA;
    endcase
end

assign s_axi_lite_awready = (writeState == AWAIT_WADD_AND_DATA || writeState == AWAIT_WADD);
assign s_axi_lite_wready  = (writeState == AWAIT_WADD_AND_DATA || writeState == AWAIT_WDATA);
assign s_axi_lite_bvalid  = (writeState == AWAIT_RESP);
assign s_axi_lite_bresp   = (writeAddr < REG_FILE_SIZE) ? AXI_OK : AXI_ERR;

// -------------------------------------------------------------------------
// one_lane_top
// Register map (all Q4.12 fixed-point unless noted):
//  [0]  control (unused for now)
//  [1]  x_min        [2]  y_min
//  [3]  x_step       [4]  y_step
//  [5]  mag0_x       [6]  mag0_y
//  [7]  mag1_x       [8]  mag1_y
//  [9]  mag2_x       [10] mag2_y
//  [11] gamma        [12] omega2
//  [13] h2           [14] mu          [15] dt
//  [16] r_settle_sq  [17] v_settle
//  [18] sum_r_settle_sq_h_sq [17:0]
//  [19] {consec_settle_count[1:0]=bits[13:12], max_steps[11:0]=bits[11:0]}
// -------------------------------------------------------------------------
wire [5:0]  fb_rd_data;
wire        frame_done;
reg  [14:0] fb_rd_addr_r;

// reg[0][0] = start trigger: computation begins on rising edge, resets on 0
wire start_w = reg_file[0][0];

one_lane_top #(
    .W(16), .F(12),
    .LUT_SIZE(1024), .LUT_ADDR_W(10),
    .Q_WIDTH(18),
    .IMG_W(160), .IMG_H(120)
) u_one_lane_top (
    .clk  (out_stream_aclk),
    .rst  (!periph_resetn),
    .start(start_w),

    .x_min (reg_file[1][15:0]),
    .y_min (reg_file[2][15:0]),
    .x_step(reg_file[3][15:0]),
    .y_step(reg_file[4][15:0]),

    .mag0_x(reg_file[5][15:0]),
    .mag0_y(reg_file[6][15:0]),
    .mag1_x(reg_file[7][15:0]),
    .mag1_y(reg_file[8][15:0]),
    .mag2_x(reg_file[9][15:0]),
    .mag2_y(reg_file[10][15:0]),

    .gamma (reg_file[11][15:0]),
    .omega2(reg_file[12][15:0]),
    .h2    (reg_file[13][15:0]),
    .mu    (reg_file[14][15:0]),
    .dt    (reg_file[15][15:0]),

    .r_settle_sq         (reg_file[16][15:0]),
    .v_settle            (reg_file[17][15:0]),
    .sum_r_settle_sq_h_sq(reg_file[18][17:0]),
    .consec_settle_count (reg_file[19][13:12]),
    .max_steps           (reg_file[19][11:0]),

    .fb_rd_addr(fb_rd_addr_r),
    .fb_rd_data(fb_rd_data),
    .frame_done(frame_done)
);

// -------------------------------------------------------------------------
// Result readout state machine
//
// After frame_done, reads BRAM sequentially (pixel 0..19199) and streams
// packed 32-bit words via AXI-Stream to VDMA.
//
// Each 32-bit word packs 4 pixel results (1 byte each, upper 2 bits zero):
//   [31:24] = pixel N+3  |  [23:16] = pixel N+2
//   [15: 8] = pixel N+1  |  [ 7: 0] = pixel N
//
// Each byte encodes: [5:2]=step_category(0-11), [1:0]=magnet_id(1-3)
//
// 19200 pixels / 4 per word = 4800 words total
// tuser[0] = SOF (start of frame, first word only)
// tlast    = EOF (last word only)
//
// Per-pixel: 2 cycles (PS_ISSUE + PS_COLLECT)
// Total: ~38400 cycles per frame readout at 100MHz = ~0.38ms
// -------------------------------------------------------------------------
localparam TOTAL_PIXELS = 15'd19200;   // 160 * 120

localparam PS_IDLE    = 3'd0;
localparam PS_ISSUE   = 3'd1;   // present address to BRAM
localparam PS_COLLECT = 3'd2;   // collect BRAM output, build word
localparam PS_SEND    = 3'd3;   // word ready, wait for tready
localparam PS_DONE    = 3'd4;   // all words sent, wait for reset

reg [2:0]  ps;
reg [14:0] px_done;      // pixels collected so far (0..19199)
reg [1:0]  bpos;         // byte position in current word (0..3)
reg [23:0] wbuf;         // accumulates bytes 0-2 of current word
reg [31:0] out_word_r;
reg        out_vld_r;
reg        out_lst_r;
reg        out_sof_r;
reg [12:0] words_sent;   // 0..4800

always @(posedge out_stream_aclk) begin
    if (!periph_resetn) begin
        ps           <= PS_IDLE;
        fb_rd_addr_r <= 0;
        px_done      <= 0;
        bpos         <= 0;
        wbuf         <= 0;
        out_word_r   <= 0;
        out_vld_r    <= 0;
        out_lst_r    <= 0;
        out_sof_r    <= 0;
        words_sent   <= 0;
    end else begin
        case (ps)

        PS_IDLE: begin
            out_vld_r  <= 0;
            px_done    <= 0;
            bpos       <= 0;
            words_sent <= 0;
            if (frame_done && start_w) begin
                fb_rd_addr_r <= 0;
                ps           <= PS_ISSUE;
            end
        end

        PS_ISSUE: begin
            // fb_rd_addr_r already set; BRAM will return data next cycle
            ps <= PS_COLLECT;
        end

        PS_COLLECT: begin
            // fb_rd_data is now valid for fb_rd_addr_r
            px_done <= px_done + 1;

            if (bpos == 2'd3) begin
                // fourth byte — complete word
                // wbuf[23:0] holds bytes 0-2 from previous cycles
                // current fb_rd_data is byte 3
                out_word_r <= {{2'b0, fb_rd_data}, wbuf};
                out_vld_r  <= 1'b1;
                out_sof_r  <= (words_sent == 0);
                out_lst_r  <= (px_done == TOTAL_PIXELS - 1);
                bpos       <= 0;
                ps         <= PS_SEND;
            end else begin
                // accumulate byte into wbuf
                case (bpos)
                    2'd0: wbuf[7:0]   <= {2'b0, fb_rd_data};
                    2'd1: wbuf[15:8]  <= {2'b0, fb_rd_data};
                    2'd2: wbuf[23:16] <= {2'b0, fb_rd_data};
                    default: ;
                endcase
                bpos         <= bpos + 1;
                fb_rd_addr_r <= fb_rd_addr_r + 1;
                ps           <= PS_ISSUE;
            end
        end

        PS_SEND: begin
            // hold tvalid until tready
            if (out_stream_tready) begin
                out_vld_r  <= 0;
                out_lst_r  <= 0;
                out_sof_r  <= 0;
                words_sent <= words_sent + 1;

                if (out_lst_r) begin
                    ps <= PS_DONE;
                end else begin
                    fb_rd_addr_r <= fb_rd_addr_r + 1;
                    ps           <= PS_ISSUE;
                end
            end
        end

        PS_DONE: begin
            out_vld_r <= 0;
            if (!start_w) ps <= PS_IDLE;  // return to idle so next start triggers a new frame
        end

        default: ps <= PS_IDLE;
        endcase
    end
end

assign out_stream_tdata  = out_word_r;
assign out_stream_tvalid = out_vld_r;
assign out_stream_tlast  = out_lst_r;
assign out_stream_tkeep  = 4'hF;
assign out_stream_tuser  = out_sof_r;

endmodule