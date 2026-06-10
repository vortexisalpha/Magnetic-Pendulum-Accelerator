// Overall 12 lanes
module pixel_generator(
    input           out_stream_aclk,
    input           s_axi_lite_aclk,
    input           axi_resetn,
    input           periph_resetn,

    output [31:0]   out_stream_tdata,
    output [3:0]    out_stream_tkeep,
    output          out_stream_tlast,
    input           out_stream_tready,
    output          out_stream_tvalid,
    output [0:0]    out_stream_tuser,

    input  [AXI_LITE_ADDR_WIDTH-1:0] s_axi_lite_araddr,
    output          s_axi_lite_arready,
    input           s_axi_lite_arvalid,
    input  [AXI_LITE_ADDR_WIDTH-1:0] s_axi_lite_awaddr,
    output          s_axi_lite_awready,
    input           s_axi_lite_awvalid, // write address valid
    input           s_axi_lite_bready,
    output [1:0]    s_axi_lite_bresp,
    output          s_axi_lite_bvalid,
    output [31:0]   s_axi_lite_rdata,
    input           s_axi_lite_rready,
    output [1:0]    s_axi_lite_rresp,
    output          s_axi_lite_rvalid,
    input  [31:0]   s_axi_lite_wdata,
    output          s_axi_lite_wready,
    input           s_axi_lite_wvalid // write data valid
);

parameter  MAX_IMG_W               = 360;
parameter MAX_IMG_H                = 360;
parameter  REG_FILE_SIZE       = 64; // increased from 32 to hold more params
localparam REG_FILE_AWIDTH     = $clog2(REG_FILE_SIZE);
parameter  AXI_LITE_ADDR_WIDTH = 8;

// derived resolution params (auto-scale with MAX_IMG_W/MAX_IMG_H)
 parameter  NUM_LANES   = 12;
  localparam TOTAL_PIX   = MAX_IMG_W * MAX_IMG_H;
  localparam SLICE_PIX_MAX = (MAX_IMG_H / NUM_LANES) * MAX_IMG_W;
  localparam PXID_W      = $clog2(TOTAL_PIX);
  localparam LOCAL_W     = $clog2(SLICE_PIX_MAX);
  localparam LANE_W      = $clog2(NUM_LANES);
  localparam TRAJ_DEPTH  = 4096;
  // width of the runtime resolution registers (holds a dimension value)
  localparam DIM_W = $clog2((MAX_IMG_W > MAX_IMG_H) ? MAX_IMG_W : MAX_IMG_H) + 1;

// elaboration-time checks for resolution sweep safety
initial begin
    if (MAX_IMG_H % NUM_LANES != 0)
        $error("MAX_IMG_H (%0d) must be divisible by NUM_LANES (%0d)", MAX_IMG_H, NUM_LANES);
    if ((MAX_IMG_W * MAX_IMG_H) % 4 != 0)
        $error("MAX_IMG_W*MAX_IMG_H must be divisible by 4 (readout packs 4 px/word)");
end

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
    if (readAddr == 5'd20)
        readData <= {31'b0, frame_done_latch};
    else if (readAddr == 5'd23) // traj_x
        readData <= {{14{traj_rd_data[35]}}, traj_rd_data[35:18]};
    else if (readAddr == 5'd24) // traj_y
        readData <= {{14{traj_rd_data[17]}}, traj_rd_data[17:0]};
    else if (readAddr == 5'd25) // traj_len
        readData <= {20'b0, traj_len};
    else if (readAddr == 5'd26)
        readData <= {31'b0, traj_done_latch};
    else if (readAddr == 5'd27) 
        readData <= {20'b0, traj_wr_addr};
    else if (readAddr == 5'd29) // fb_rd_data
        readData <= {26'b0, fb_rd_data};
    else
        readData <= reg_file[readAddr]; // reads data from the reg_file
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

// write only when both data and address are valid
always @(posedge s_axi_lite_aclk) begin
    if (!axi_resetn) begin
        writeState <= AWAIT_WADD_AND_DATA;
    end else case (writeState)
        AWAIT_WADD_AND_DATA: begin
            case ({s_axi_lite_awvalid, s_axi_lite_wvalid})
                2'b10: begin writeAddr <= s_axi_lite_awaddr[2+:REG_FILE_AWIDTH]; writeState <= AWAIT_WDATA; end
                2'b01: begin writeData <= s_axi_lite_wdata;                      writeState <= AWAIT_WADD;  end
                2'b11: begin writeData <= s_axi_lite_wdata; writeAddr <= s_axi_lite_awaddr[2+:REG_FILE_AWIDTH]; writeState <= AWAIT_WRITE; end
                default: writeState <= AWAIT_WADD_AND_DATA;
            endcase
        end
        AWAIT_WDATA: if (s_axi_lite_wvalid)  begin writeData <= s_axi_lite_wdata;                      writeState <= AWAIT_WRITE; end
        AWAIT_WADD:  if (s_axi_lite_awvalid) begin writeAddr <= s_axi_lite_awaddr[2+:REG_FILE_AWIDTH]; writeState <= AWAIT_WRITE; end
        AWAIT_WRITE: begin reg_file[writeAddr] <= writeData; writeState <= AWAIT_RESP; end
        AWAIT_RESP:  if (s_axi_lite_bready) writeState <= AWAIT_WADD_AND_DATA;
        default:     writeState <= AWAIT_WADD_AND_DATA;
    endcase
end

assign s_axi_lite_awready = (writeState == AWAIT_WADD_AND_DATA || writeState == AWAIT_WADD);
assign s_axi_lite_wready  = (writeState == AWAIT_WADD_AND_DATA || writeState == AWAIT_WDATA);
assign s_axi_lite_bvalid  = (writeState == AWAIT_RESP);
assign s_axi_lite_bresp   = (writeAddr < REG_FILE_SIZE) ? AXI_OK : AXI_ERR;

// -------------------------------------------------------------------------
// Register map (Q4.14):
//  [0]  control (bit[0]=start)   [1] x_min  [2] y_min  [3] x_step  [4] y_step
//  [5..10] mag0..2 x/y           [11] gamma [12] omega2 [13] h2
//  [14] mu  [15] dt              [16] r_settle_sq  [17] v_settle
//  [18] sum_r_settle_sq_h_sq     [19] {consec_settle_count[13:12], max_steps[11:0]}
//  [20] debug: frame_done_latch
//  [30] mag_active[2:0] : bit i = 1 -> magnet i active
// -------------------------------------------------------------------------
// trajectory inputs
// [21] traj_px_id
// [22] traj_rd_addr

// [23]     traj_x
// [24]     traj_y
// [25]     traj_len
// [26]     traj_done
// [27]     traj_wr_addr for debug

// resolution inputs
// [31] img_w
// [32] img_h
// [33] slice_h (software computes img_h / NUM_LANES to save DSP)
// [34] slice_pixels (img_w * slice_h)

wire start_w = reg_file[0][0];
wire bram_rd_mode = reg_file[0][1];

//mag_active extract
wire [2:0] mag_active = reg_file[30][2:0];

// -------------------------------------------------------------------------
// 12 lane instances
// -------------------------------------------------------------------------
wire [LOCAL_W-1:0] fb_rd_addr_local;

wire [5:0] fb_rd_data_0, fb_rd_data_1, fb_rd_data_2, fb_rd_data_3;
wire [5:0] fb_rd_data_4, fb_rd_data_5, fb_rd_data_6, fb_rd_data_7;
wire[5: 0] fb_rd_data_8, fb_rd_data_9, fb_rd_data_10, fb_rd_data_11;
wire frame_done_0, frame_done_1, frame_done_2, frame_done_3;
wire frame_done_4, frame_done_5, frame_done_6, frame_done_7;
wire frame_done_8, frame_done_9, frame_done_10, frame_done_11;


// trajectory inputs
wire [PXID_W-1:0] traj_px_id = reg_file[21][PXID_W-1:0];

wire traj_valid_0, traj_valid_1, traj_valid_2, traj_valid_3, traj_valid_4, traj_valid_5, traj_valid_6, traj_valid_7, traj_valid_8;
wire traj_valid_9, traj_valid_10, traj_valid_11;
wire signed [17:0] traj_x_0, traj_x_1, traj_x_2, traj_x_3, traj_x_4, traj_x_5, traj_x_6, traj_x_7, traj_x_8;
wire signed [17:0] traj_x_9, traj_x_10, traj_x_11;
wire signed [17:0] traj_y_0, traj_y_1, traj_y_2, traj_y_3, traj_y_4, traj_y_5, traj_y_6, traj_y_7, traj_y_8;
wire signed [17:0] traj_y_9, traj_y_10, traj_y_11;
wire traj_done_0, traj_done_1, traj_done_2, traj_done_3, traj_done_4, traj_done_5, traj_done_6, traj_done_7, traj_done_8;
wire traj_done_9, traj_done_10, traj_done_11;

// resolution inputs
wire [DIM_W-1:0]  img_w_w     = reg_file[31][DIM_W-1:0];
wire [DIM_W-1:0]  img_h_w     = reg_file[32][DIM_W-1:0];
wire [DIM_W-1:0]  slice_h_w   = reg_file[33][DIM_W-1:0];
wire [LOCAL_W:0]  slice_pix_w = reg_file[34][LOCAL_W:0];

wire [PXID_W-1:0] total_pix_w = NUM_LANES * slice_pix_w;


// Shared connection macro (all lanes get identical physics registers)
// similar to #define in C
// ID = lane ID, FD = Frame Done, FRD = Frame-buffer Read Data
`define LANE_CONNECT(ID, FD, FRD) \
one_lane_top #( \
    .W(18), .F(14), .LUT_SIZE(4096), .LUT_ADDR_W(12), .Q_WIDTH(18), \
    .IMG_W(MAX_IMG_W), .IMG_H(MAX_IMG_H), .LANE_ID(ID), .NUM_LANES(12) \
) u_lane``ID ( \
    .clk(out_stream_aclk), .rst(!periph_resetn), .start(start_w), \
    .img_w(img_w_w), .img_h(img_h_w), .slice_h(slice_h_w), .slice_pixels(slice_pix_w), \
    .x_min(reg_file[1][17:0]),  .y_min(reg_file[2][17:0]), \
    .x_step(reg_file[3][17:0]), .y_step(reg_file[4][17:0]), \
    .mag0_x(reg_file[5][17:0]), .mag0_y(reg_file[6][17:0]), \
    .mag1_x(reg_file[7][17:0]), .mag1_y(reg_file[8][17:0]), \
    .mag2_x(reg_file[9][17:0]), .mag2_y(reg_file[10][17:0]), \
    .mag_active(mag_active), \
    .gamma(reg_file[11][17:0]), .omega2(reg_file[12][17:0]), \
    .h2(reg_file[13][17:0]),    .mu(reg_file[14][17:0]),    .dt(reg_file[15][17:0]), \
    .r_settle_sq(reg_file[16][17:0]), .v_settle(reg_file[17][17:0]), \
    .sum_r_settle_sq_h_sq(reg_file[18][17:0]), \
    .consec_settle_count(reg_file[19][13:12]), .max_steps(reg_file[19][11:0]), \
    .fb_rd_addr(fb_rd_addr_local), .fb_rd_data(FRD), .frame_done(FD), \
    .traj_px_id(traj_px_id), \
    .traj_valid(traj_valid_``ID), \
    .traj_x(traj_x_``ID), .traj_y(traj_y_``ID), \
    .traj_done(traj_done_``ID) \
)

`LANE_CONNECT(0, frame_done_0, fb_rd_data_0);
`LANE_CONNECT(1, frame_done_1, fb_rd_data_1);
`LANE_CONNECT(2, frame_done_2, fb_rd_data_2);
`LANE_CONNECT(3, frame_done_3, fb_rd_data_3);
`LANE_CONNECT(4, frame_done_4, fb_rd_data_4);
`LANE_CONNECT(5, frame_done_5, fb_rd_data_5);
`LANE_CONNECT(6, frame_done_6, fb_rd_data_6);
`LANE_CONNECT(7, frame_done_7, fb_rd_data_7);
`LANE_CONNECT(8, frame_done_8, fb_rd_data_8);
`LANE_CONNECT(9, frame_done_9, fb_rd_data_9);
`LANE_CONNECT(10, frame_done_10, fb_rd_data_10);
`LANE_CONNECT(11, frame_done_11, fb_rd_data_11);

// -------------------------------------------------------------------------
// frame_done latch - all 12 lanes must finish
// -------------------------------------------------------------------------
wire frame_done_all = frame_done_0 & frame_done_1 & frame_done_2 & frame_done_3
                    & frame_done_4 & frame_done_5 & frame_done_6 & frame_done_7 & frame_done_8
                    & frame_done_9 & frame_done_10 & frame_done_11;
reg  frame_done_latch = 1'b0;

always @(posedge out_stream_aclk) begin
    if (!periph_resetn || !start_w)
        frame_done_latch <= 1'b0;
    else if (frame_done_all)
        frame_done_latch <= 1'b1;
end

// Mux to determine lane to which trajectory pixel belongs, get its x and y values
reg signed [17:0] traj_x_sel, traj_y_sel;
always @(*) begin
    if (traj_valid_0) begin
        traj_x_sel = traj_x_0;
        traj_y_sel = traj_y_0;
    end else if (traj_valid_1) begin
        traj_x_sel = traj_x_1;
        traj_y_sel = traj_y_1;
    end else if (traj_valid_2) begin
        traj_x_sel = traj_x_2;
        traj_y_sel = traj_y_2;
    end else if (traj_valid_3) begin
        traj_x_sel = traj_x_3;
        traj_y_sel = traj_y_3;
    end else if (traj_valid_4) begin
        traj_x_sel = traj_x_4;
        traj_y_sel = traj_y_4;
    end else if (traj_valid_5) begin
        traj_x_sel = traj_x_5;
        traj_y_sel = traj_y_5;
    end else if (traj_valid_6) begin
        traj_x_sel = traj_x_6;
        traj_y_sel = traj_y_6;
    end else if (traj_valid_7) begin
        traj_x_sel = traj_x_7;
        traj_y_sel = traj_y_7;
    end else if (traj_valid_8) begin
        traj_x_sel = traj_x_8;
        traj_y_sel = traj_y_8;
    end else if (traj_valid_9) begin
        traj_x_sel = traj_x_9;
        traj_y_sel = traj_y_9;
    end else if (traj_valid_10) begin
        traj_x_sel = traj_x_10;
        traj_y_sel = traj_y_10;
    end else if (traj_valid_11) begin
        traj_x_sel = traj_x_11;
         traj_y_sel = traj_y_11;
    end else begin
        traj_x_sel = 18'sd0;   // no lane valid this cycle (not written anyway)
        traj_y_sel = 18'sd0;
    end
end


wire traj_valid_any, traj_done_any;

assign traj_valid_any = traj_valid_0 | traj_valid_1 | traj_valid_2 | traj_valid_3 |
                        traj_valid_4 | traj_valid_5 | traj_valid_6 | traj_valid_7 |
                        traj_valid_8 | traj_valid_9 | traj_valid_10 | traj_valid_11;
assign traj_done_any = traj_done_0 | traj_done_1 | traj_done_2 | traj_done_3 |
                       traj_done_4 | traj_done_5 | traj_done_6 | traj_done_7 |
                       traj_done_8 | traj_done_9 | traj_done_10 | traj_done_11;

reg [11:0] traj_wr_addr;     // current write address = number of points so far
reg [11:0] traj_len;         // total steps
reg        traj_done_latch;

always @(posedge out_stream_aclk) begin
    if (!periph_resetn || !start_w) begin
        traj_wr_addr    <= 12'd0;
        traj_len        <= 12'd0;
        traj_done_latch <= 1'b0;
    end else begin
        if (traj_valid_any && !traj_done_latch)
            traj_wr_addr <= traj_wr_addr + 12'd1;
        if (traj_done_any && !traj_done_latch) begin
            traj_len <= traj_wr_addr + 12'd1;
            traj_done_latch <= 1'b1;
        end
    end
end

// trajectory BRAM: stores {x, y} = 36 bits, depth = max trajectory length
wire [35:0] traj_rd_data;
frame_buffer #(
    .FB_W   (1),
    .FB_H   (TRAJ_DEPTH),
    .DATA_W (36)
) u_traj_buf (
    .clk     (out_stream_aclk),
    .rst     (!periph_resetn),
    .wr_en   (traj_valid_any && !traj_done_latch),
    .wr_addr (traj_wr_addr),
    .wr_data ({traj_x_sel, traj_y_sel}),   // x in [35:18], y in [17:0]
    .rd_addr (reg_file[22][11:0]),         // CPU sets which step to read
    .rd_data (traj_rd_data)
);
// -------------------------------------------------------------------------
// Read-address decode
// Lane N covers pixel IDs [N*2400 .. (N+1)*2400-1]
// fb_rd_addr_local (12-bit, 0..2399) broadcast to all lanes; output muxed
// by registered lane_sel_r to match 1-cycle BRAM read latency.
// -------------------------------------------------------------------------
reg [PXID_W-1:0] fb_rd_addr_r;  // global pixel index, 0 to 19199

// find lane and local index: generated loop scales with NUM_LANES & resolution
 // add. These regs are maintained in lockstep with fb_rd_addr_r in the FSM below.
  reg [LANE_W-1:0]  rd_lane;        // lane that owns the current fb_rd_addr_r
  reg [PXID_W-1:0]  rd_lane_base;   // first global index of that lane
  reg [PXID_W-1:0]  rd_next_base;   // first global index of the NEXT lane

  assign fb_rd_addr_local = fb_rd_addr_r - rd_lane_base;

  reg [LANE_W-1:0] lane_sel_r;
  always @(posedge out_stream_aclk) begin
      if (!periph_resetn) lane_sel_r <= {LANE_W{1'b0}};
      else lane_sel_r <= rd_lane;
  end

reg [5:0] fb_rd_data;
// based on the location, find corresponding lane that contains relevant output
always @(*) begin
    case (lane_sel_r)
        4'd0:    fb_rd_data = fb_rd_data_0;
        4'd1:    fb_rd_data = fb_rd_data_1;
        4'd2:    fb_rd_data = fb_rd_data_2;
        4'd3:    fb_rd_data = fb_rd_data_3;
        4'd4:    fb_rd_data = fb_rd_data_4;
        4'd5:    fb_rd_data = fb_rd_data_5;
        4'd6:    fb_rd_data = fb_rd_data_6;
        4'd7:    fb_rd_data = fb_rd_data_7;
        4'd8:    fb_rd_data = fb_rd_data_8;
        4'd9:    fb_rd_data = fb_rd_data_9;
        4'd10:   fb_rd_data = fb_rd_data_10;
        default: fb_rd_data = fb_rd_data_11;
    endcase
end

// -------------------------------------------------------------------------
// Result readout FSM - unchanged; still reads 0..19199, 4800 words
// -------------------------------------------------------------------------
// Once all eight lanes have finished, walk the whole 19,200-pixel frame in order, 
// pack four pixels into each 32-bit word, and push those words out over AXI-Stream to the DMA

localparam PS_IDLE    = 3'd0;
localparam PS_ISSUE   = 3'd1;
localparam PS_COLLECT = 3'd2;
localparam PS_SEND    = 3'd3;
localparam PS_DONE    = 3'd4;

reg [2:0]  ps;
reg [PXID_W-1:0] px_done; // count of pixels processed
reg [1:0]  bpos; // byte position in the word, 0 ... 3
reg [23:0] wbuf; // holds the first 3 bytes of each word while 4th byte is computed, to send tgt
reg [31:0] out_word_r;
reg        out_vld_r;
reg        out_lst_r;
reg [PXID_W-1:0] words_sent;

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
        words_sent   <= 0;
        rd_lane      <= 0;
        rd_lane_base <= 0;
        rd_next_base <= slice_pix_w;
    end else begin
        case (ps)
        
        // Idle state until frame is done
          PS_IDLE: begin
              out_vld_r  <= 0;
              px_done    <= 0;
              bpos       <= 0;
              words_sent <= 0;
              if (bram_rd_mode) begin
                  // Debug random read: software supplies LOCAL addr + lane directly
                  // (no hardware divide). reg[28] = { lane@bit16 , local_addr@bit0 }
                  fb_rd_addr_r <= {{(PXID_W-LOCAL_W){1'b0}}, reg_file[28][LOCAL_W-1:0]};
                  rd_lane      <= reg_file[28][16 +: LANE_W];
                  rd_lane_base <= 0;              // -> fb_rd_addr_local == local addr
                  rd_next_base <= slice_pix_w;
              end
              else if (frame_done_latch && start_w) begin
                  fb_rd_addr_r <= 0;
                  rd_lane      <= 0;
                  rd_lane_base <= 0;
                  rd_next_base <= slice_pix_w;
                  ps           <= PS_ISSUE;
              end
          end

        // One cycle wait to absorb BRAM's one cycle read latency
        PS_ISSUE: begin
            ps <= PS_COLLECT;
        end

        // Collect pixel info
        PS_COLLECT: begin
            px_done <= px_done + 1;
            // if on 4th byte of the word, take all four bytes and send
            if (bpos == 2'd3) begin
                out_word_r <= {{2'b0, fb_rd_data}, wbuf};
                out_vld_r  <= 1'b1;
                out_lst_r  <= (px_done == total_pix_w - 1); // signals if whoe frame is processed
                bpos <= 0; // start on 0 byte index for new word
                ps <= PS_SEND; // send the frame
            end else begin
            // write to correct byte index after padding 6 bits output to 8 bits
                case (bpos)
                    2'd0: wbuf[7:0]   <= {2'b0, fb_rd_data};
                    2'd1: wbuf[15:8]  <= {2'b0, fb_rd_data};
                    2'd2: wbuf[23:16] <= {2'b0, fb_rd_data};
                    default: ;
                endcase
                bpos         <= bpos + 1;
                fb_rd_addr_r <= fb_rd_addr_r + 1;
                if ((fb_rd_addr_r + 1'b1) >= rd_next_base) begin   // crossed into next lane, need to advance
                    rd_lane      <= rd_lane + 1'b1;
                    rd_lane_base <= rd_next_base;
                    rd_next_base <= rd_next_base + slice_pix_w;
                end
                ps <= PS_ISSUE;
            end
        end

        PS_SEND: begin
            if (!start_w) begin
                out_vld_r <= 0; out_lst_r <= 0;
                ps        <= PS_IDLE;
            // the FSM stalls in PS_SEND as long as tready is low, so back-pressure from the DMA is respected and no word is lost
            end else if (out_stream_tready) begin
                out_vld_r  <= 0; out_lst_r <= 0;
                words_sent <= words_sent + 1;
                if (out_lst_r) begin
                    ps <= PS_DONE;
                end else begin
                    fb_rd_addr_r <= fb_rd_addr_r + 1;
                    if ((fb_rd_addr_r + 1'b1) >= rd_next_base) begin   // crossed into next lane
                        rd_lane      <= rd_lane + 1'b1;
                        rd_lane_base <= rd_next_base;
                        rd_next_base <= rd_next_base + slice_pix_w;
                    end
                    ps           <= PS_ISSUE;
                end
            end
        end

        PS_DONE: begin
            out_vld_r <= 0;
            if (!start_w) ps <= PS_IDLE;
        end

        default: ps <= PS_IDLE;
        endcase
    end
end

assign out_stream_tdata  = out_word_r;
assign out_stream_tvalid = out_vld_r;
assign out_stream_tlast  = out_lst_r;
assign out_stream_tkeep  = 4'hF;
assign out_stream_tuser  = 1'b0;

endmodule