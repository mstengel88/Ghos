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
    },
    quoteAddressAutocomplete: (() => {
        let googlePlacesPromise = null;
        const instances = new Map();

        const load = (apiKey) => {
            if (globalThis.google?.maps?.places) {
                return Promise.resolve();
            }

            if (googlePlacesPromise) {
                return googlePlacesPromise;
            }

            googlePlacesPromise = new Promise((resolve, reject) => {
                const existing = document.querySelector(
                    'script[data-google-places="true"]');
                if (existing) {
                    existing.addEventListener("load", resolve, { once: true });
                    existing.addEventListener(
                        "error",
                        () => reject(
                            new Error("Failed to load Google Places.")),
                        { once: true });
                    return;
                }

                const script = document.createElement("script");
                script.src =
                    "https://maps.googleapis.com/maps/api/js?key=" +
                    encodeURIComponent(apiKey) +
                    "&libraries=places";
                script.async = true;
                script.defer = true;
                script.dataset.googlePlaces = "true";
                script.addEventListener("load", () => {
                    if (globalThis.google?.maps?.places) {
                        resolve();
                        return;
                    }

                    reject(new Error(
                        "Google Places loaded without the Places library."));
                }, { once: true });
                script.addEventListener(
                    "error",
                    () => reject(
                        new Error("Failed to load Google Places.")),
                    { once: true });
                document.head.append(script);
            });

            return googlePlacesPromise;
        };

        const componentValue = (components, type, shortName = false) => {
            const component = components.find(
                (candidate) => candidate.types?.includes(type));
            if (!component) {
                return "";
            }

            return shortName
                ? component.short_name || component.long_name || ""
                : component.long_name || "";
        };

        return {
            attach: async (options, dotNetReference) => {
                await load(options.apiKey);

                const addressInput =
                    document.getElementById(options.address1Id);
                if (!addressInput) {
                    throw new Error("The delivery address input was not found.");
                }

                const existing = instances.get(options.address1Id);
                if (existing) {
                    globalThis.google.maps.event.removeListener(
                        existing.listener);
                }

                const autocomplete =
                    new globalThis.google.maps.places.Autocomplete(
                        addressInput,
                        {
                            types: ["address"],
                            componentRestrictions: { country: ["us"] },
                            fields: [
                                "address_components",
                                "formatted_address"
                            ]
                        });
                const listener = autocomplete.addListener(
                    "place_changed",
                    async () => {
                        const place = autocomplete.getPlace();
                        const components = place?.address_components || [];
                        const streetNumber =
                            componentValue(
                                components,
                                "street_number");
                        const route =
                            componentValue(components, "route");
                        const city =
                            componentValue(components, "locality") ||
                            componentValue(
                                components,
                                "postal_town") ||
                            componentValue(
                                components,
                                "sublocality_level_1");
                        const selection = {
                            addressLine1:
                                [streetNumber, route]
                                    .filter(Boolean)
                                    .join(" ")
                                    .trim(),
                            city,
                            state: componentValue(
                                components,
                                "administrative_area_level_1",
                                true),
                            postalCode: componentValue(
                                components,
                                "postal_code"),
                            country:
                                componentValue(
                                    components,
                                    "country",
                                    true) || "US"
                        };

                        await dotNetReference.invokeMethodAsync(
                            "ApplyGoogleAddressAsync",
                            selection);
                    });

                instances.set(
                    options.address1Id,
                    { autocomplete, listener, dotNetReference });
            },
            detach: (address1Id) => {
                const existing = instances.get(address1Id);
                if (!existing) {
                    return;
                }

                globalThis.google?.maps?.event?.removeListener(
                    existing.listener);
                instances.delete(address1Id);
            }
        };
    })()
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
