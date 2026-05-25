`timescale 1ns/1ps

module tb_nearest_magnet_s3;

    localparam int W       = 16;
    localparam int F       = 12;
    localparam int Q_WIDTH = 18;

    // nearest_magnet_s3 is one registered pipeline stage
    localparam int DUT_LATENCY = 1;

    logic clk;
    logic rst;
    logic in_valid;

    logic signed [Q_WIDTH-1:0] q0, q1, q2;

    logic signed [W-1:0] in_dx0, in_dy0;
    logic signed [W-1:0] in_dx1, in_dy1;
    logic signed [W-1:0] in_dx2, in_dy2;

    logic signed [W-1:0] in_x, in_y;
    logic signed [W-1:0] in_vx, in_vy;

    logic [11:0] in_step_cnt;
    logic [14:0] in_id;
    logic [1:0]  in_settle_count;

    logic out_valid;
    logic [1:0] nearest_magnet_id;
    logic signed [Q_WIDTH-1:0] min_q;

    logic signed [W-1:0] out_dx0, out_dy0;
    logic signed [W-1:0] out_dx1, out_dy1;
    logic signed [W-1:0] out_dx2, out_dy2;

    logic signed [W-1:0] out_x, out_y;
    logic signed [W-1:0] out_vx, out_vy;

    logic [11:0] out_step_cnt;
    logic [14:0] out_id;
    logic [1:0]  out_settle_count;

    logic signed [Q_WIDTH-1:0] out_q0, out_q1, out_q2;

    nearest_magnet_s3 #(
        .W(W),
        .F(F),
        .Q_WIDTH(Q_WIDTH)
    ) dut (
        .clk(clk),
        .rst(rst),
        .in_valid(in_valid),

        .q0(q0),
        .q1(q1),
        .q2(q2),

        .in_dx0(in_dx0),
        .in_dy0(in_dy0),
        .in_dx1(in_dx1),
        .in_dy1(in_dy1),
        .in_dx2(in_dx2),
        .in_dy2(in_dy2),

        .in_x(in_x),
        .in_y(in_y),

        .in_vx(in_vx),
        .in_vy(in_vy),

        .in_step_cnt(in_step_cnt),
        .in_id(in_id),
        .in_settle_count(in_settle_count),

        .out_valid(out_valid),
        .nearest_magnet_id(nearest_magnet_id),
        .min_q(min_q),

        .out_dx0(out_dx0),
        .out_dy0(out_dy0),
        .out_dx1(out_dx1),
        .out_dy1(out_dy1),
        .out_dx2(out_dx2),
        .out_dy2(out_dy2),

        .out_x(out_x),
        .out_y(out_y),

        .out_step_cnt(out_step_cnt),

        .out_vx(out_vx),
        .out_vy(out_vy),

        .out_id(out_id),
        .out_settle_count(out_settle_count),

        .out_q0(out_q0),
        .out_q1(out_q1),
        .out_q2(out_q2)
    );

    // 100 MHz clock
    initial begin
        clk = 1'b0;
        forever #5 clk = ~clk;
    end

    task automatic fail(input string msg);
        begin
            $display("ERROR: %s", msg);
            $finish;
        end
    endtask

    task automatic initialise_inputs;
        begin
            in_valid = 1'b0;

            q0 = '0;
            q1 = '0;
            q2 = '0;

            in_dx0 = '0;
            in_dy0 = '0;
            in_dx1 = '0;
            in_dy1 = '0;
            in_dx2 = '0;
            in_dy2 = '0;

            in_x = '0;
            in_y = '0;

            in_vx = '0;
            in_vy = '0;

            in_step_cnt = '0;
            in_id = '0;
            in_settle_count = '0;
        end
    endtask

    task automatic drive_input(
        input logic                      valid_i,
        input logic signed [Q_WIDTH-1:0] q0_i,
        input logic signed [Q_WIDTH-1:0] q1_i,
        input logic signed [Q_WIDTH-1:0] q2_i
    );
        begin
            @(negedge clk);

            in_valid <= valid_i;

            q0 <= q0_i;
            q1 <= q1_i;
            q2 <= q2_i;

            // Distinct pass-through values
            in_dx0 <= 16'sd1;
            in_dy0 <= 16'sd2;
            in_dx1 <= 16'sd3;
            in_dy1 <= 16'sd4;
            in_dx2 <= 16'sd5;
            in_dy2 <= 16'sd6;

            in_x <= 16'sd100;
            in_y <= -16'sd50;

            in_vx <= 16'sd7;
            in_vy <= -16'sd8;

            in_step_cnt <= 12'd25;
            in_id <= 15'd1234;
            in_settle_count <= 2'd2;
        end
    endtask

    task automatic wait_for_output;
        begin
            repeat (DUT_LATENCY) @(posedge clk);
            #1;
        end
    endtask

    task automatic check_common_passthrough(input string tc_name);
        begin
            if (out_dx0 !== 16'sd1)        fail({tc_name, ": out_dx0 mismatch"});
            if (out_dy0 !== 16'sd2)        fail({tc_name, ": out_dy0 mismatch"});
            if (out_dx1 !== 16'sd3)        fail({tc_name, ": out_dx1 mismatch"});
            if (out_dy1 !== 16'sd4)        fail({tc_name, ": out_dy1 mismatch"});
            if (out_dx2 !== 16'sd5)        fail({tc_name, ": out_dx2 mismatch"});
            if (out_dy2 !== 16'sd6)        fail({tc_name, ": out_dy2 mismatch"});

            if (out_x !== 16'sd100)        fail({tc_name, ": out_x mismatch"});
            if (out_y !== -16'sd50)        fail({tc_name, ": out_y mismatch"});

            if (out_vx !== 16'sd7)         fail({tc_name, ": out_vx mismatch"});
            if (out_vy !== -16'sd8)        fail({tc_name, ": out_vy mismatch"});

            if (out_step_cnt !== 12'd25)   fail({tc_name, ": out_step_cnt mismatch"});
            if (out_id !== 15'd1234)       fail({tc_name, ": out_id mismatch"});
            if (out_settle_count !== 2'd2) fail({tc_name, ": out_settle_count mismatch"});
        end
    endtask

    task automatic run_valid_test(
        input string                      tc_name,
        input logic signed [Q_WIDTH-1:0] q0_i,
        input logic signed [Q_WIDTH-1:0] q1_i,
        input logic signed [Q_WIDTH-1:0] q2_i,
        input logic [1:0]                expected_id,
        input logic signed [Q_WIDTH-1:0] expected_min_q
    );
        begin
            drive_input(1'b1, q0_i, q1_i, q2_i);

            wait_for_output();

            if (out_valid !== 1'b1)
                fail({tc_name, ": out_valid should be 1"});

            if (nearest_magnet_id !== expected_id) begin
                $display("%s: expected nearest=%0d, got=%0d",
                         tc_name, expected_id, nearest_magnet_id);
                fail({tc_name, ": nearest_magnet_id mismatch"});
            end

            if (min_q !== expected_min_q) begin
                $display("%s: expected min_q=%0d, got=%0d",
                         tc_name, expected_min_q, min_q);
                fail({tc_name, ": min_q mismatch"});
            end

            if (out_q0 !== q0_i) fail({tc_name, ": out_q0 mismatch"});
            if (out_q1 !== q1_i) fail({tc_name, ": out_q1 mismatch"});
            if (out_q2 !== q2_i) fail({tc_name, ": out_q2 mismatch"});

            check_common_passthrough(tc_name);

            $display("PASS: %s", tc_name);
        end
    endtask

    task automatic run_invalid_test;
        begin
            drive_input(
                1'b0,
                19'sd10,
                19'sd20,
                19'sd30
            );

            wait_for_output();

            if (out_valid !== 1'b0)
                fail("TC_INVALID: out_valid should be 0");

            if (nearest_magnet_id !== 2'd0)
                fail("TC_INVALID: nearest_magnet_id should be 0");

            if (min_q !== '0)
                fail("TC_INVALID: min_q should be 0");

            $display("PASS: TC_INVALID");
        end
    endtask

    initial begin
        $dumpfile("tb_nearest_magnet_s3.vcd");
        $dumpvars(0, tb_nearest_magnet_s3);

        rst = 1'b1;
        initialise_inputs();

        repeat (3) @(posedge clk);

        @(negedge clk);
        rst = 1'b0;

        // Your current nearest_magnet_s3 encoding:
        // q0 minimum -> nearest_magnet_id = 1
        // q1 minimum -> nearest_magnet_id = 2
        // q2 minimum -> nearest_magnet_id = 3

        run_valid_test(
            "TC1 q0 minimum",
            19'sd10,
            19'sd20,
            19'sd30,
            2'd1,
            19'sd10
        );

        run_valid_test(
            "TC2 q1 minimum",
            19'sd50,
            19'sd5,
            19'sd40,
            2'd2,
            19'sd5
        );

        run_valid_test(
            "TC3 q2 minimum",
            19'sd100,
            19'sd80,
            19'sd1,
            2'd3,
            19'sd1
        );

        // Tie priority based on your module:
        // if q0 <= q1 and q0 <= q2, q0 wins.
        run_valid_test(
            "TC4 tie q0 q1",
            19'sd15,
            19'sd15,
            19'sd25,
            2'd1,
            19'sd15
        );

        run_valid_test(
            "TC5 tie q0 q2",
            19'sd12,
            19'sd25,
            19'sd12,
            2'd1,
            19'sd12
        );

        // if q1 <= q2, q1 wins.
        run_valid_test(
            "TC6 tie q1 q2",
            19'sd40,
            19'sd7,
            19'sd7,
            2'd2,
            19'sd7
        );

        run_valid_test(
            "TC7 all equal",
            19'sd9,
            19'sd9,
            19'sd9,
            2'd1,
            19'sd9
        );

        run_invalid_test();

        $display("All nearest_magnet_s3 tests passed.");

        #20;
        $finish;
    end

endmodule