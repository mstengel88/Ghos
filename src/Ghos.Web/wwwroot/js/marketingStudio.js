const sessions = new Map();

export function attach(rootId, dotNetReference) {
    detach(rootId);

    const root = document.getElementById(rootId);
    if (!root) {
        return;
    }

    const state = {
        root,
        dotNetReference,
        active: null
    };

    state.pointerDown = event => {
        const element = event.target.closest("[data-layout-element]");
        if (!element || !root.contains(element)) {
            return;
        }

        const card = element.closest("[data-layout-card]");
        if (!card) {
            return;
        }

        event.preventDefault();
        const cardRect = card.getBoundingClientRect();
        state.active = {
            element,
            elementKey: element.dataset.layoutElement,
            startClientX: event.clientX,
            startClientY: event.clientY,
            startX: Number(element.dataset.layoutX || 0),
            startY: Number(element.dataset.layoutY || 0),
            canvasWidth: Number(card.dataset.canvasWidth || 1080),
            canvasHeight: Number(card.dataset.canvasHeight || 1080),
            cardWidth: cardRect.width,
            cardHeight: cardRect.height
        };

        root.querySelectorAll("[data-layout-element].drag-selected")
            .forEach(item => item.classList.remove("drag-selected"));
        element.classList.add("drag-selected");
        element.setPointerCapture?.(event.pointerId);
    };

    state.pointerMove = event => {
        if (!state.active) {
            return;
        }

        event.preventDefault();
        const active = state.active;
        const x = active.startX +
            ((event.clientX - active.startClientX) *
                active.canvasWidth / active.cardWidth);
        const y = active.startY +
            ((event.clientY - active.startClientY) *
                active.canvasHeight / active.cardHeight);
        active.x = Math.max(-600, Math.min(600, x));
        active.y = Math.max(-900, Math.min(900, y));
        active.element.style.left =
            `${active.x / active.canvasWidth * 100}%`;
        active.element.style.top =
            `${active.y / active.canvasHeight * 100}%`;
    };

    state.pointerUp = async event => {
        if (!state.active) {
            return;
        }

        event.preventDefault();
        const active = state.active;
        state.active = null;
        active.element.classList.remove("drag-selected");
        await state.dotNetReference.invokeMethodAsync(
            "UpdateLayoutPosition",
            active.elementKey,
            active.x ?? active.startX,
            active.y ?? active.startY);
    };

    root.addEventListener("pointerdown", state.pointerDown);
    window.addEventListener("pointermove", state.pointerMove, {
        passive: false
    });
    window.addEventListener("pointerup", state.pointerUp, {
        passive: false
    });
    sessions.set(rootId, state);
}

export function detach(rootId) {
    const state = sessions.get(rootId);
    if (!state) {
        return;
    }

    state.root.removeEventListener("pointerdown", state.pointerDown);
    window.removeEventListener("pointermove", state.pointerMove);
    window.removeEventListener("pointerup", state.pointerUp);
    sessions.delete(rootId);
}
