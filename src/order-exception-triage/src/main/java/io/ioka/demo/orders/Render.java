// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.orders;

import java.awt.*;
import java.awt.image.BufferedImage;
import java.nio.file.*;
import javax.imageio.ImageIO;

/** Terminal/output composition using actual persisted packets, rendered with Java2D. */
public final class Render {
    private Render() {}
    public static void run(Path packets, Path output) throws Exception {
        var packet = FilesUtil.read(packets.resolve("event-001.json"));
        var answer = packet.path("answer");
        var image = new BufferedImage(1400, 880, BufferedImage.TYPE_INT_RGB);
        var g = image.createGraphics();
        g.setRenderingHint(RenderingHints.KEY_TEXT_ANTIALIASING, RenderingHints.VALUE_TEXT_ANTIALIAS_ON);
        g.setColor(new Color(14, 23, 38)); g.fillRect(0, 0, 1400, 880);
        g.setColor(new Color(109, 224, 189)); g.setFont(new Font("SansSerif", Font.BOLD, 34));
        g.drawString("ORDER EXCEPTION TRIAGE", 54, 68);
        g.setColor(new Color(175, 190, 210)); g.setFont(new Font("SansSerif", Font.PLAIN, 20));
        g.drawString("Java 21  /  Munarium Server 1.1.1  /  Fictional fulfillment workflow", 54, 107);
        g.setColor(new Color(24, 37, 55)); g.fillRoundRect(40, 140, 1320, 270, 18, 18);
        g.setFont(new Font("Monospaced", Font.PLAIN, 21)); g.setColor(new Color(224, 234, 244));
        String[] lines = {"$ order-exception-triage consume", "event-001  complete  -> review outbox", "Proposed team: " + answer.path("route").asText(), "Order execution: awaiting human review", "Duplicate delivery: existing packet reused", "Evidence: isolated procedure, source identity and content hash"};
        for (int i = 0; i < lines.length; i++) g.drawString(lines[i], 65, 180 + i * 38);
        g.setFont(new Font("SansSerif", Font.BOLD, 25)); g.setColor(new Color(109, 224, 189)); g.drawString("Persisted review packet — event-001", 54, 465);
        g.setFont(new Font("SansSerif", Font.PLAIN, 23)); g.setColor(new Color(224, 234, 244));
        String text = answer.path("explanation").asText();
        int y = 510; StringBuilder line = new StringBuilder();
        for (String word : text.split("\\s+")) {
            if (g.getFontMetrics().stringWidth(line + word) > 1250) { g.drawString(line.toString(), 54, y); y += 34; line.setLength(0); }
            line.append(word).append(' ');
        }
        g.drawString(line.toString(), 54, y);
        g.setColor(new Color(175, 190, 210)); g.setFont(new Font("Monospaced", Font.PLAIN, 16));
        g.drawString("Provider: " + packet.path("evidence").path("completion").path("provider").asText() + "  Model: " + packet.path("evidence").path("completion").path("model").asText(), 54, 755);
        g.drawString("Packet: JSON evidence + Markdown | Durable inbox: H2 | Bounded workers: 2", 54, 790);
        g.setFont(new Font("SansSerif", Font.PLAIN, 17)); g.drawString("Rendering of actual controlled-test output; canned fixture responses, no local inference.", 54, 843);
        g.dispose(); Files.createDirectories(output.toAbsolutePath().getParent()); ImageIO.write(image, "png", output.toFile());
    }
}
