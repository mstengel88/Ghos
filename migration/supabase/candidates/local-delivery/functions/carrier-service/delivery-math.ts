const METERS_PER_MILE = 1609.34;

export function calculateDriveTimeCost(
  oneWaySeconds: number,
  distanceMeters: number,
  ratePerMinute: number,
) {
  const safeSeconds = Math.max(0, oneWaySeconds);
  const safeDistanceMeters = Math.max(0, distanceMeters);
  const safeRatePerMinute = Math.max(0, ratePerMinute);
  const roundTripMinutes = (safeSeconds * 2) / 60;

  return {
    costDollars:
      Math.round(roundTripMinutes * safeRatePerMinute * 100) / 100,
    oneWayMiles:
      Math.round((safeDistanceMeters / METERS_PER_MILE) * 10) / 10,
    roundTripMinutes,
  };
}
