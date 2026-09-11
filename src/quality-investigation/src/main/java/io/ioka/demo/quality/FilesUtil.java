// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.quality;

import com.fasterxml.jackson.databind.JsonNode;
import io.ioka.munarium.client.model.Json;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.security.MessageDigest;
import java.util.HexFormat;

public final class FilesUtil {
    private FilesUtil() {}
    public static String json(Object value) throws Exception { return Json.MAPPER.copy().setSerializationInclusion(com.fasterxml.jackson.annotation.JsonInclude.Include.ALWAYS).writer().with(com.fasterxml.jackson.databind.SerializationFeature.ORDER_MAP_ENTRIES_BY_KEYS).writeValueAsString(value); }
    public static JsonNode read(Path path) throws Exception { return Json.MAPPER.readTree(Files.readString(path)); }
    public static String hash(String text) throws Exception {
        return HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(text.getBytes(StandardCharsets.UTF_8)));
    }
    public static void save(Path path, Object value) throws Exception { write(path, json(value) + "\n"); }
    public static void write(Path path, String text) throws Exception {
        Files.createDirectories(path.toAbsolutePath().getParent());
        Path temporary = Files.createTempFile(path.toAbsolutePath().getParent(), ".quality-", ".tmp");
        try {
            try (var channel = java.nio.channels.FileChannel.open(temporary, StandardOpenOption.WRITE)) {
                var bytes = java.nio.ByteBuffer.wrap(text.getBytes(StandardCharsets.UTF_8));
                while (bytes.hasRemaining()) channel.write(bytes);
                channel.force(true);
            }
            Files.move(temporary, path, StandardCopyOption.ATOMIC_MOVE, StandardCopyOption.REPLACE_EXISTING);
        } finally { Files.deleteIfExists(temporary); }
    }
    public static String env(String key, String fallback) { return System.getenv().getOrDefault(key, fallback); }
}
