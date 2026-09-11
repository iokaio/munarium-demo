// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.quality;
import java.awt.*;
import java.awt.image.BufferedImage;
import java.nio.file.*;
import java.util.ArrayList;
import javax.imageio.ImageIO;

public final class Render {
    private Render() {}
    public static void run(Path input,Path output) throws Exception {
        var lines=new ArrayList<String>();
        for(String line:Files.readAllLines(input)) {while(line.length()>112) {lines.add(line.substring(0,112));line=line.substring(112);}lines.add(line);}
        var image=new BufferedImage(1440,Math.max(520,80+lines.size()*30),BufferedImage.TYPE_INT_RGB);var graphics=image.createGraphics();
        graphics.setColor(new Color(18,30,44));graphics.fillRect(0,0,image.getWidth(),image.getHeight());graphics.setFont(new Font("Monospaced",Font.PLAIN,20));graphics.setRenderingHint(RenderingHints.KEY_TEXT_ANTIALIASING,RenderingHints.VALUE_TEXT_ANTIALIAS_ON);
        int y=45;for(String line:lines) {graphics.setColor(y==45?new Color(109,224,189):new Color(224,234,244));graphics.drawString(line,30,y);y+=30;}
        graphics.dispose();Files.createDirectories(output.toAbsolutePath().getParent());ImageIO.write(image,"png",output.toFile());
    }
}
