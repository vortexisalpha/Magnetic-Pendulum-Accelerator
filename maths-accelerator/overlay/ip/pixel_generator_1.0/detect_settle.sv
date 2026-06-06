module detect_settle (
    input logic rst,

    // vals from last stage of lane
    input logic valid,
    input logic [1:0] settle_count, // passed in & incremented in lane S2 / S3
    input logic [11:0] step_cnt, //might need to change later to 13 bits if we want to run up to 5000 time steps

    // settle cond
    input logic [1:0] consec_settle_count, // number of consecutive settled time steps to declare pixel as settled

    // time_out
    input logic [11:0] max_steps, // might need to change later to 13 bits, same reason

    output logic settled,
    output logic time_out

    // These outputs can be used to control a MUX at the output of the lane,
    // if settled & ~ time_out, lane feeds pixel to output FIFO, marking it with nearest magnet idx
    // elif ~ settled & ~time_out, lane feeds pixel back to S1,
    // elif ~ settled & time_out, lane feeds pixel to output FIFO, marking it as unsettled


    // actual implementation then:
    // if settled ^ time_out: MUX output to FIFO
    // else: output back to S1

);

    always_comb begin
        if (rst) begin
            settled = 0;
            time_out = 0;
        end

        else if (valid) begin
            settled = (settle_count >= consec_settle_count);
            time_out = ~settled && (step_cnt >= max_steps);
        end

        else begin
            settled = 0;
            time_out = 0;
        end
    end
endmodule



    
