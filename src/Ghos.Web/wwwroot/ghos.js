globalThis.ghos = {
    copyText: async (value) => {
        const text = String(value ?? "");
        if (navigator.clipboard && globalThis.isSecureContext) {
            await navigator.clipboard.writeText(text);
            return;
        }

        const input = document.createElement("textarea");
        input.value = text;
        input.style.position = "fixed";
        input.style.opacity = "0";
        input.setAttribute("readonly", "");
        document.body.append(input);
        input.select();
        document.execCommand("copy");
        input.remove();
    }
};
