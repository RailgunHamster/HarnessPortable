package com.harness.portable

import java.util.concurrent.CopyOnWriteArraySet

/**
 * Process-local visibility signal shared by the activity and the tunnel
 * service. Used to refresh keepalive / wake-lock policy when the UI is
 * shown or sent to the background.
 */
internal object AppVisibility {

    @Volatile
    var isVisible: Boolean = false
        private set

    private val listeners = CopyOnWriteArraySet<(Boolean) -> Unit>()

    fun setVisible(visible: Boolean) {
        if (isVisible == visible) return
        isVisible = visible
        listeners.forEach { it(visible) }
    }

    fun addListener(listener: (Boolean) -> Unit) {
        listeners.add(listener)
        listener(isVisible)
    }

    fun removeListener(listener: (Boolean) -> Unit) {
        listeners.remove(listener)
    }
}
