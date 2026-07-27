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

(() => {
    let deferredInstallPrompt = null;
    let waitingWorker = null;
    let refreshing = false;

    const getActionButton = () =>
        document.getElementById("ghos-pwa-action");

    const hideAction = () => {
        const button = getActionButton();
        if (button) {
            button.hidden = true;
            button.removeAttribute("data-pwa-action");
        }
    };

    const showAction = (label, action) => {
        const button = getActionButton();
        if (!button) {
            return;
        }

        button.textContent = label;
        button.dataset.pwaAction = action;
        button.hidden = false;
    };

    const isStandalone = () =>
        globalThis.matchMedia("(display-mode: standalone)").matches ||
        globalThis.navigator.standalone === true;

    const isIos = () =>
        /iphone|ipad|ipod/i.test(globalThis.navigator.userAgent);

    const handleAction = async () => {
        const button = getActionButton();
        const action = button?.dataset.pwaAction;

        if (action === "install" && deferredInstallPrompt) {
            await deferredInstallPrompt.prompt();
            await deferredInstallPrompt.userChoice;
            deferredInstallPrompt = null;
            hideAction();
            return;
        }

        if (action === "update" && waitingWorker) {
            button.disabled = true;
            button.textContent = "Updating…";
            waitingWorker.postMessage({ type: "SKIP_WAITING" });
            return;
        }

        if (action === "ios-help") {
            globalThis.alert(
                "To install GHOS on this device, open the browser Share menu and choose “Add to Home Screen.”");
        }
    };

    const connectActionButton = () => {
        const button = getActionButton();
        if (!button || button.dataset.pwaConnected === "true") {
            return;
        }

        button.dataset.pwaConnected = "true";
        button.addEventListener("click", handleAction);
    };

    const announceWaitingWorker = (worker) => {
        waitingWorker = worker;
        connectActionButton();
        showAction("Update GHOS", "update");
    };

    globalThis.addEventListener("beforeinstallprompt", (event) => {
        event.preventDefault();
        deferredInstallPrompt = event;
        connectActionButton();
        showAction("Install GHOS", "install");
    });

    globalThis.addEventListener("appinstalled", () => {
        deferredInstallPrompt = null;
        hideAction();
    });

    globalThis.navigator.serviceWorker?.addEventListener(
        "controllerchange",
        () => {
            if (refreshing) {
                return;
            }

            refreshing = true;
            globalThis.location.reload();
        });

    globalThis.addEventListener("load", async () => {
        connectActionButton();

        if (isIos() && !isStandalone()) {
            showAction("Install help", "ios-help");
        }

        if (!("serviceWorker" in globalThis.navigator) ||
            !globalThis.isSecureContext) {
            return;
        }

        try {
            const registration =
                await globalThis.navigator.serviceWorker.register(
                    "/service-worker.js",
                    { scope: "/" });

            if (registration.waiting &&
                globalThis.navigator.serviceWorker.controller) {
                announceWaitingWorker(registration.waiting);
            }

            registration.addEventListener("updatefound", () => {
                const worker = registration.installing;
                if (!worker) {
                    return;
                }

                worker.addEventListener("statechange", () => {
                    if (worker.state === "installed" &&
                        globalThis.navigator.serviceWorker.controller) {
                        announceWaitingWorker(worker);
                    }
                });
            });
        } catch (error) {
            console.warn("GHOS PWA registration was unavailable.", error);
        }
    });
})();
