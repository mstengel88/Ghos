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
    downloadText: (filename, value, contentType = "text/plain;charset=utf-8") => {
        const blob = new Blob([String(value ?? "")], { type: contentType });
        const url = URL.createObjectURL(blob);
        const link = document.createElement("a");
        link.href = url;
        link.download = String(filename || "download.txt");
        link.style.display = "none";
        document.body.append(link);
        link.click();
        link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 0);
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
                const suggestions =
                    document.getElementById(options.suggestionsId);
                if (!addressInput) {
                    throw new Error("The delivery address input was not found.");
                }
                if (!suggestions) {
                    throw new Error(
                        "The address suggestion container was not found.");
                }

                const existing = instances.get(options.address1Id);
                if (existing) {
                    existing.dispose();
                }

                const autocompleteService =
                    new globalThis.google.maps.places.AutocompleteService();
                const placesService =
                    new globalThis.google.maps.places.PlacesService(
                        document.createElement("div"));
                let sessionToken =
                    new globalThis.google.maps.places.AutocompleteSessionToken();
                let debounceTimer = 0;
                let requestNumber = 0;

                const hideSuggestions = () => {
                    suggestions.replaceChildren();
                    suggestions.hidden = true;
                };

                const selectPrediction = (prediction) => {
                    placesService.getDetails(
                        {
                            placeId: prediction.place_id,
                            fields: [
                                "address_components",
                                "formatted_address"
                            ],
                            sessionToken
                        },
                        async (place, status) => {
                            if (status !==
                                    globalThis.google.maps.places
                                        .PlacesServiceStatus.OK ||
                                !place) {
                                return;
                            }

                            const components =
                                place?.address_components || [];
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

                            addressInput.value =
                                selection.addressLine1 ||
                                place.formatted_address ||
                                prediction.description;
                            hideSuggestions();
                            sessionToken =
                                new globalThis.google.maps.places
                                    .AutocompleteSessionToken();
                            await dotNetReference.invokeMethodAsync(
                                "ApplyGoogleAddressAsync",
                                selection);
                        });
                };

                const renderPredictions = (predictions) => {
                    suggestions.replaceChildren();
                    if (!predictions?.length) {
                        suggestions.hidden = true;
                        return;
                    }

                    for (const prediction of predictions) {
                        const button = document.createElement("button");
                        button.type = "button";
                        button.className = "address-suggestion";
                        button.setAttribute("role", "option");

                        const main = document.createElement("strong");
                        main.textContent =
                            prediction.structured_formatting?.main_text ||
                            prediction.description;
                        const secondary = document.createElement("span");
                        secondary.textContent =
                            prediction.structured_formatting?.secondary_text ||
                            "";
                        button.append(main, secondary);
                        button.addEventListener(
                            "pointerdown",
                            (event) => event.preventDefault());
                        button.addEventListener(
                            "click",
                            () => selectPrediction(prediction));
                        suggestions.append(button);
                    }

                    suggestions.hidden = false;
                };

                const requestPredictions = () => {
                    globalThis.clearTimeout(debounceTimer);
                    const query = addressInput.value.trim();
                    if (query.length < 3) {
                        requestNumber += 1;
                        hideSuggestions();
                        return;
                    }

                    debounceTimer = globalThis.setTimeout(() => {
                        const currentRequest = ++requestNumber;
                        autocompleteService.getPlacePredictions(
                            {
                                input: query,
                                componentRestrictions: { country: "us" },
                                types: ["address"],
                                sessionToken
                            },
                            (predictions, status) => {
                                if (currentRequest !== requestNumber) {
                                    return;
                                }

                                if (status !==
                                    globalThis.google.maps.places
                                        .PlacesServiceStatus.OK) {
                                    hideSuggestions();
                                    return;
                                }

                                renderPredictions(predictions);
                            });
                    }, 225);
                };

                const handleBlur = () => {
                    globalThis.setTimeout(hideSuggestions, 150);
                };
                const handleKeydown = (event) => {
                    if (event.key === "Escape") {
                        hideSuggestions();
                    }
                };

                addressInput.addEventListener("input", requestPredictions);
                addressInput.addEventListener("blur", handleBlur);
                addressInput.addEventListener("keydown", handleKeydown);

                const dispose = () => {
                    globalThis.clearTimeout(debounceTimer);
                    addressInput.removeEventListener(
                        "input",
                        requestPredictions);
                    addressInput.removeEventListener("blur", handleBlur);
                    addressInput.removeEventListener(
                        "keydown",
                        handleKeydown);
                    hideSuggestions();
                };

                instances.set(
                    options.address1Id,
                    { dispose, dotNetReference });
            },
            detach: (address1Id) => {
                const existing = instances.get(address1Id);
                if (!existing) {
                    return;
                }

                existing.dispose();
                instances.delete(address1Id);
            }
        };
    })()
};

(() => {
    const minimumHeight = 720;
    const maximumHeight = 12000;

    globalThis.addEventListener("message", (event) => {
        const message = event.data;
        if (!message ||
            (message.type !== "ghos:ticket-creator:resize" &&
                message.type !== "ghos:ticket-creator:scroll")) {
            return;
        }

        const frame = Array.from(
            document.querySelectorAll(
                "iframe[data-ghos-ticketing-frame]"))
            .find((candidate) =>
                candidate.contentWindow === event.source);

        if (!frame) {
            return;
        }

        try {
            if (new URL(frame.src).origin !== event.origin) {
                return;
            }
        } catch {
            return;
        }

        if (message.type === "ghos:ticket-creator:scroll") {
            const deltaY = Number.isFinite(message.deltaY)
                ? message.deltaY
                : 0;
            const deltaX = Number.isFinite(message.deltaX)
                ? message.deltaX
                : 0;

            globalThis.scrollBy({
                top: deltaY,
                left: deltaX,
                behavior: "auto"
            });
            return;
        }

        if (!Number.isFinite(message.height)) {
            return;
        }

        const height = Math.min(
            maximumHeight,
            Math.max(minimumHeight, Math.ceil(message.height)));
        frame.style.height = `${height}px`;
        frame.dataset.embeddedPath =
            typeof message.path === "string" ? message.path : "";
    });
})();

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
