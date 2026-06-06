`timescale 1ns/1ps

module tb_detect_settle;

    logic        rst;
    logic        valid;
    logic [1:0]  settle_count;
    logic [11:0] step_cnt;
    logic [1:0]  consec_settle_count;
    logic [11:0] max_steps;
    logic        settled;
    logic        time_out;

    detect_settle dut (
        .rst                 (rst),
        .valid               (valid),
        .settle_count        (settle_count),
        .step_cnt            (step_cnt),
        .consec_settle_count (consec_settle_count),
        .max_steps           (max_steps),
        .settled             (settled),
        .time_out            (time_out)
    );


    int pass_count = 0;
    int fail_count = 0;

    task automatic check(
        input string  label,
        input logic   exp_settled,
        input logic   exp_timeout
    );
        #1; // let combinational settle
        if (settled == exp_settled && time_out == exp_timeout) begin
            $display("PASS  %-52s  settled=%0b timeout=%0b",
                     label, settled, time_out);
            pass_count++;
        end 
        else begin
            $display("FAIL  %-52s  settled=%0b(exp %0b) timeout=%0b(exp %0b)",
                     label, settled, exp_settled, time_out, exp_timeout);
            fail_count++;
        end
    endtask

    task automatic apply(
        input logic        i_rst,
        input logic        i_valid,
        input logic [1:0]  i_settle_count,
        input logic [11:0] i_step_cnt,
        input logic [1:0]  i_consec,
        input logic [11:0] i_max_steps
    );
        rst                  = i_rst;
        valid                = i_valid;
        settle_count         = i_settle_count;
        step_cnt             = i_step_cnt;
        consec_settle_count  = i_consec;
        max_steps            = i_max_steps;
    endtask

    // tests
    initial begin
        $dumpfile("tb_detect_settle.vcd");
        $dumpvars(0, tb_detect_settle);

        $display("=== detect_settle testbench ===\n");

        // 1. reset
        apply(1, 1, 3, 100,  1, 2000); check("rst=1 valid=1 count=3 → both 0",           0, 0);
        apply(1, 0, 3, 2000, 1, 2000); check("rst=1 valid=0 count=3 step=max → both 0",  0, 0);
        apply(1, 1, 3, 2001, 3, 2000); check("rst=1 count=3 step>max consec=3 → both 0", 0, 0);

        // 2. valid low
        apply(0, 0, 3,  100,  1, 2000); check("valid=0 count=3 → both 0",                0, 0);
        apply(0, 0, 0,  2001, 1, 2000); check("valid=0 step>max → both 0",               0, 0);

        // 3. computing state
        apply(0, 1, 0, 0,    1, 2000); check("count=0 step=0 → computing",               0, 0);
        apply(0, 1, 0, 1999, 1, 2000); check("count=0 step=1999 → computing",            0, 0);
        apply(0, 1, 0, 100,  3, 2000); check("count=0 consec=3 → computing",             0, 0);
        apply(0, 1, 2, 100,  3, 2000); check("count=2 consec=3 → computing (need 1)",    0, 0);

        // 4. settled condition
        apply(0, 1, 1, 500,  1, 2000); check("count=1 consec=1 → settled",               1, 0);
        apply(0, 1, 3, 500,  1, 2000); check("count=3 consec=1 → settled (exceeds)",     1, 0);
        apply(0, 1, 3, 500,  3, 2000); check("count=3 consec=3 → settled (exact)",       1, 0);
        apply(0, 1, 3, 500,  2, 2000); check("count=3 consec=2 → settled (exceeds)",     1, 0);

        // 5. timeout condition
        apply(0, 1, 0, 2000, 1, 2000); check("count=0 step=max → timeout",               0, 1);
        apply(0, 1, 0, 2001, 1, 2000); check("count=0 step>max → timeout",               0, 1);
        apply(0, 1, 0, 2000, 3, 2000); check("count=0 step=max consec=3 → timeout",      0, 1);

        // 6. settled takes priority over timeout
        apply(0, 1, 1, 2000, 1, 2000); check("count=1 step=max consec=1 → settled wins", 1, 0);
        apply(0, 1, 3, 2000, 3, 2000); check("count=3 step=max consec=3 → settled wins", 1, 0);
        apply(0, 1, 3, 2001, 1, 2000); check("count=3 step>max consec=1 → settled wins", 1, 0);

        // 7. mutual exclusivity sweep, outputs can't both be high at same time
        $display("\n--- mutual exclusivity sweep ---");
        begin
            int errors = 0;
            for (int c = 0; c <= 3; c++) begin
                for (int s = 0; s <= 4096; s += 50) begin
                    apply(0, 1, c[1:0], s[11:0], 2, 2000);
                    #1;
                    if (settled && time_out) begin
                        $display("FAIL  both high: count=%0d step=%0d", c, s);
                        errors++;
                    end
                end
            end
            if (errors == 0) begin
                $display("PASS  settled and time_out never both high");
                pass_count++;
            end else
                fail_count++;
        end

        // sweep summary
        $display("\n=== results: %0d passed, %0d failed ===", pass_count, fail_count);
        if (fail_count == 0)
            $display("ALL TESTS PASSED");
        else
            $display("SOME TESTS FAILED");

        $finish;
    end

endmodule
