module colour_encoder #(
    parameter LABEL_W = 2,
    parameter RGB_W = 8
)(
    input  logic [LABEL_W-1:0]  label,
    input  logic    active_video,

    output logic [RGB_W-1:0]    red,
    output logic [RGB_W-1:0]    green,
    output logic [RGB_W-1:0]    blue
);

    always_comb begin
        // Default colour: black
        red = 8'h00;
        green = 8'h00;
        blue = 8'h00;

        if (active_video) begin
            case (label[1:0])
                2'd1: begin
                    // Magnet 1: red
                    red = 8'hFF;
                    green = 8'h00;
                    blue = 8'h00;
                end

                2'd2: begin
                    // Magnet 2: green
                    red = 8'h00;
                    green = 8'hFF;
                    blue = 8'h00;
                end

                2'd3: begin
                    // Magnet 3: blue
                    red = 8'h00;
                    green = 8'h00;
                    blue = 8'hFF;
                end

                default: begin
                    // label = 0 or invalid: black
                    red = 8'h00;
                    green = 8'h00;
                    blue = 8'h00;
                end
            endcase
        end
    end

endmodule