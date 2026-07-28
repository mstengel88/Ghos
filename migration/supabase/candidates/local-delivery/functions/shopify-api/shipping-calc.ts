// Shipping calculation logic for edge function (mirrors src/lib/shipping-data.ts)

export interface ShippingZone {
  id: string;
  name: string;
  baseRate: number;
  fuelSurcharge: number;
  minCharge: number;
}

export interface TruckType {
  id: string;
  name: string;
  maxWeight: number;
  maxVolume: number;
  costPerMile: number;
  flatFee: number;
}

export const SHIPPING_ZONES: ShippingZone[] = [
  { id: "local", name: "Local (0-50 mi)", baseRate: 3.5, fuelSurcharge: 0.15, minCharge: 150 },
  { id: "regional", name: "Regional (50-200 mi)", baseRate: 2.8, fuelSurcharge: 0.18, minCharge: 350 },
  { id: "long", name: "Long Haul (200-500 mi)", baseRate: 2.2, fuelSurcharge: 0.22, minCharge: 800 },
  { id: "cross", name: "Cross Country (500+ mi)", baseRate: 1.85, fuelSurcharge: 0.25, minCharge: 1500 },
];

export const TRUCK_TYPES: TruckType[] = [
  { id: "pickup", name: "Pickup Truck", maxWeight: 2000, maxVolume: 2, costPerMile: 1.2, flatFee: 75 },
  { id: "single", name: "Single Axle Dump", maxWeight: 13000, maxVolume: 10, costPerMile: 2.5, flatFee: 150 },
  { id: "tandem", name: "Tandem Axle Dump", maxWeight: 26000, maxVolume: 18, costPerMile: 3.8, flatFee: 225 },
  { id: "semi", name: "Semi Trailer", maxWeight: 44000, maxVolume: 25, costPerMile: 4.5, flatFee: 350 },
  { id: "super", name: "Super Dump", maxWeight: 40000, maxVolume: 22, costPerMile: 5.2, flatFee: 400 },
];

export function getZoneByDistance(miles: number): ShippingZone {
  if (miles <= 50) return SHIPPING_ZONES[0];
  if (miles <= 200) return SHIPPING_ZONES[1];
  if (miles <= 500) return SHIPPING_ZONES[2];
  return SHIPPING_ZONES[3];
}

export function calculateShippingCost(
  distanceMiles: number,
  weightLbs: number,
  truckType: TruckType
): { baseCost: number; fuelSurcharge: number; total: number } {
  const zone = getZoneByDistance(distanceMiles);
  const baseCost = Math.max(
    distanceMiles * truckType.costPerMile + truckType.flatFee,
    zone.minCharge
  );
  const fuelSurcharge = baseCost * zone.fuelSurcharge;
  return {
    baseCost: Math.round(baseCost * 100) / 100,
    fuelSurcharge: Math.round(fuelSurcharge * 100) / 100,
    total: Math.round((baseCost + fuelSurcharge) * 100) / 100,
  };
}
