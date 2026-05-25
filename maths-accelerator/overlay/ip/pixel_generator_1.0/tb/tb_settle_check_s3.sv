`timescale 1ns/1ps

module tb_settle_check_s3;

    localparam int W       = 16;
    localparam int F       = 12;
    localparam int Q_WIDTH = 18;

    // Change this if settle_check_s3 has more than 1 registered stage
    localparam int DUT_LATENCY = 1;

    logic clk;
    logic rst;
    logic in_valid;

    logic signed [W-1:0] in_dx0, in_dy0;
    logic signed [W-1:0] in_dx1, in_dy1;
    logic signed [W-1:0] in_dx2, in_dy2;

    logic signed [W-1:0] in_x, in_y;
    logic signed [W-1:0] in_vx, in_vy;

    logic [11:0] in_step_cnt;
    logic [14:0] in_id;
    logic [1:0]  in_settle_count;

    logic signed [Q_WIDTH-1:0] in_q0, in_q1, in_q2;

    logic [1:0] in_nearest_magnet_id;
    logic signed [Q_WIDTH-1:0] min_q;

    logic signed [Q_WIDTH-1:0] sum_r_settle_sq_h_sq;
    logic [W-1:0] v_settle;

    // Outputs
    logic out_valid;

    logic signed [W-1:0] out_dx0, out_dy0;
    logic signed [W-1:0] out_dx1, out_dy1;
    logic signed [W-1:0] out_dx2, out_dy2;

    logic signed [W-1:0] out_x, out_y;
    logic signed [W-1:0] out_vx, out_vy;

    logic [11:0] out_step_cnt;
    logic [14:0] out_id;

    logic [1:0] out_nearest_magnet_id;
    logic [1:0] out_settle_count;

    logic signed [Q_WIDTH-1:0] out_q0, out_q1, out_q2;

    settle_check_s3 #(
        .W(W),
        .F(F),
        .Q_WIDTH(Q_WIDTH)
    ) dut (
        .clk(clk),
        .rst(rst),
        .in_valid(in_valid),

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

        .in_q0(in_q0),
        .in_q1(in_q1),
        .in_q2(in_q2),

        .in_nearest_magnet_id(in_nearest_magnet_id),
        .min_q(min_q),

        .sum_r_settle_sq_h_sq(sum_r_settle_sq_h_sq),
        .v_settle(v_settle),

        .out_valid(out_valid),

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

        .out_nearest_magnet_id(out_nearest_magnet_id),
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

            in_q0 = '0;
            in_q1 = '0;
            in_q2 = '0;

            in_nearest_magnet_id = '0;
            min_q = '0;

            sum_r_settle_sq_h_sq = 19'sd100;
            v_settle = 16'd10;
        end
    endtask

    task automatic drive_input(
        input logic                       valid_i,
        input logic signed [Q_WIDTH-1:0]  min_q_i,
        input logic signed [W-1:0]        vx_i,
        input logic signed [W-1:0]        vy_i,
        input logic [1:0]                 settle_count_i,
        input logic [1:0]                 nearest_id_i
    );
        begin
            @(negedge clk);

            in_valid <= valid_i;

            min_q <= min_q_i;

            in_vx <= vx_i;
            in_vy <= vy_i;

            in_settle_count <= settle_count_i;
            in_nearest_magnet_id <= nearest_id_i;

            // Distinct pass-through values
            in_dx0 <= 16'sd1;
            in_dy0 <= 16'sd2;
            in_dx1 <= 16'sd3;
            in_dy1 <= 16'sd4;
            in_dx2 <= 16'sd5;
            in_dy2 <= 16'sd6;

            in_x <= 16'sd100;
            in_y <= -16'sd100;

            in_step_cnt <= 12'd55;
            in_id <= 15'd999;

            in_q0 <= 19'sd11;
            in_q1 <= 19'sd22;
            in_q2 <= 19'sd33;
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
            if (out_dx0 !== 16'sd1)      fail({tc_name, ": out_dx0 mismatch"});
            if (out_dy0 !== 16'sd2)      fail({tc_name, ": out_dy0 mismatch"});
            if (out_dx1 !== 16'sd3)      fail({tc_name, ": out_dx1 mismatch"});
            if (out_dy1 !== 16'sd4)      fail({tc_name, ": out_dy1 mismatch"});
            if (out_dx2 !== 16'sd5)      fail({tc_name, ": out_dx2 mismatch"});
            if (out_dy2 !== 16'sd6)      fail({tc_name, ": out_dy2 mismatch"});

            if (out_x !== 16'sd100)      fail({tc_name, ": out_x mismatch"});
            if (out_y !== -16'sd100)     fail({tc_name, ": out_y mismatch"});

            if (out_step_cnt !== 12'd55) fail({tc_name, ": out_step_cnt mismatch"});
            if (out_id !== 15'd999)      fail({tc_name, ": out_id mismatch"});

            if (out_q0 !== 19'sd11)      fail({tc_name, ": out_q0 mismatch"});
            if (out_q1 !== 19'sd22)      fail({tc_name, ": out_q1 mismatch"});
            if (out_q2 !== 19'sd33)      fail({tc_name, ": out_q2 mismatch"});
        end
    endtask

    task automatic run_valid_test(
        input string                      tc_name,
        input logic signed [Q_WIDTH-1:0]  min_q_i,
        input logic signed [W-1:0]        vx_i,
        input logic signed [W-1:0]        vy_i,
        input logic [1:0]                 settle_count_i,
        input logic [1:0]                 nearest_id_i,
        input logic [1:0]                 expected_settle_count
    );
        begin
            drive_input(
                1'b1,
                min_q_i,
                vx_i,
                vy_i,
                settle_count_i,
                nearest_id_i
            );

            wait_for_output();

            if (out_valid !== 1'b1) begin
                fail({tc_name, ": out_valid should be 1"});
            end

            if (out_settle_count !== expected_settle_count) begin
                $display("%s: expected settle_count=%0d, got=%0d",
                         tc_name, expected_settle_count, out_settle_count);
                fail({tc_name, ": settle_count mismatch"});
            end

            if (out_vx !== vx_i) begin
                fail({tc_name, ": out_vx mismatch"});
            end

            if (out_vy !== vy_i) begin
                fail({tc_name, ": out_vy mismatch"});
            end

            if (out_nearest_magnet_id !== nearest_id_i) begin
                fail({tc_name, ": nearest magnet ID mismatch"});
            end

            check_common_passthrough(tc_name);

            $display("PASS: %s", tc_name);
        end
    endtask

    task automatic run_invalid_test;
        begin
            drive_input(
                1'b0,
                19'sd50,
                16'sd1,
                16'sd1,
                2'd2,
                2'd1
            );

            wait_for_output();

            if (out_valid !== 1'b0) begin
                fail("TC_INVALID: out_valid should be 0");
            end

            // If your DUT defines invalid output settle_count as 0, keep this check.
            // If invalid output data is don't-care, remove this check.
            if (out_settle_count !== 2'd0) begin
                fail("TC_INVALID: out_settle_count should be 0");
            end

            $display("PASS: TC_INVALID");
        end
    endtask

    initial begin
        $dumpfile("tb_settle_check_s3.vcd");
        $dumpvars(0, tb_settle_check_s3);

        rst = 1'b1;
        initialise_inputs();

        repeat (3) @(posedge clk);

        @(negedge clk);
        rst = 1'b0;

        // Thresholds:
        // sum_r_settle_sq_h_sq = 100
        // v_settle = 10
        //
        // Condition is assumed to be:
        // min_q < threshold
        // abs(vx) < v_settle
        // abs(vy) < v_settle

        run_valid_test(
            "TC1 increment 0 to 1",
            19'sd50,
            16'sd3,
            -16'sd4,
            2'd0,
            2'd2,
            2'd1
        );

        run_valid_test(
            "TC2 increment 1 to 2",
            19'sd20,
            16'sd1,
            16'sd2,
            2'd1,
            2'd1,
            2'd2
        );

        run_valid_test(
            "TC3 saturate at 3",
            19'sd10,
            16'sd1,
            16'sd1,
            2'd3,
            2'd0,
            2'd3
        );

        run_valid_test(
            "TC4 velocity x too high resets count",
            19'sd10,
            16'sd20,
            16'sd1,
            2'd2,
            2'd1,
            2'd0
        );

        run_valid_test(
            "TC5 velocity y too high resets count",
            19'sd10,
            16'sd1,
            -16'sd20,
            2'd2,
            2'd1,
            2'd0
        );

        run_valid_test(
            "TC6 q too large resets count",
            19'sd150,
            16'sd1,
            16'sd1,
            2'd2,
            2'd1,
            2'd0
        );

        run_valid_test(
            "TC7 q equal threshold resets count if using <",
            19'sd100,
            16'sd1,
            16'sd1,
            2'd2,
            2'd1,
            2'd0
        );

        run_valid_test(
            "TC8 vx equal threshold resets count if using <",
            19'sd50,
            16'sd10,
            16'sd1,
            2'd2,
            2'd1,
            2'd0
        );

        run_invalid_test();

        $display("All settle_check_s3 tests passed.");
        #20;
        $finish;
    end

endmodule