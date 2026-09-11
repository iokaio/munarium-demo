// SPDX-License-Identifier: Apache-2.0
package io.ioka.demo.orders;

public record OrderEvent(String eventId, String orderId, String reason, int requested, int available,
                         boolean consent, boolean documentsPresent) {
    public OrderEvent {
        if (eventId == null || !eventId.matches("event-[a-z0-9-]{1,60}") || orderId == null || !orderId.matches("order-[a-z0-9-]{1,60}"))
            throw new IllegalArgumentException("Invalid event or order identifier.");
        if (reason == null || !reason.matches("[a-z_]{1,40}") || requested < 1 || available < 0)
            throw new IllegalArgumentException("Malformed order facts.");
    }
    public String query() throws Exception {
        return "Fictional order exception. Explain the hold and propose the responsible team using the procedure. "
            + "Keep order execution unchanged. Event facts: " + FilesUtil.json(this);
    }
}
