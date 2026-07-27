namespace Ghos.Web.DumpSite;

public static class DumpSiteDefaults
{
    public const string ItemMappingsJson = """
        {
          "Yard Waste|Pickup Truck": {
            "sku": "YARD_WASTE|PICK-UP TRUCK",
            "name": "Dump Site - Yard Waste - Pickup Truck",
            "price": 35,
            "tax": 0
          },
          "Yard Waste|Dump Trl / Truck": {
            "sku": "YARD_WASTE|DUMP TRL / TRK",
            "name": "Dump Site - Yard Waste - Dump Trailer or Truck",
            "price": 50,
            "tax": 0
          },
          "Yard Waste|TDM/Quad Axle": {
            "sku": "YARD_WASTE|TDM / QUAD AXLE",
            "name": "Dump Site - Yard Waste - TDM or Quad Axle",
            "price": 110,
            "tax": 0
          },
          "Clean Fill|Pickup Truck": {
            "sku": "CLEAN_FILL|PICK-UP TRUCK",
            "name": "Dump Site - Clean Fill - Pickup Truck",
            "price": 35,
            "tax": 0
          },
          "Clean Fill|Dump Trl / Truck": {
            "sku": "CLEAN_FILL|DUMP TRL / TRK",
            "name": "Dump Site - Clean Fill - Dump Trailer or Truck",
            "price": 60,
            "tax": 0
          },
          "Clean Fill|TDM/Quad Axle": {
            "sku": "CLEAN_FILL|TDM / QUAD AXLE",
            "name": "Dump Site - Clean Fill - TDM or Quad Axle",
            "price": 250,
            "tax": 0
          },
          "Mixed Load (Soil/Sod/Grindings)|Pickup Truck": {
            "sku": "MIXED_FILL|PICK-UP TRUCK",
            "name": "Dump Site - Mixed Load - Pickup Truck",
            "price": 35,
            "tax": 0
          },
          "Mixed Load (Soil/Sod/Grindings)|Dump Trl / Truck": {
            "sku": "MIXED_FILL|DUMP TRL / TRK",
            "name": "Dump Site - Mixed Load - Dump Trailer or Truck",
            "price": 80,
            "tax": 0
          },
          "Mixed Load (Soil/Sod/Grindings)|TDM/Quad Axle": {
            "sku": "MIXED_FILL|TDM / QUAD AXLE",
            "name": "Dump Site - Mixed Load - TDM or Quad Axle",
            "price": 250,
            "tax": 0
          },
          "Brush and Limbs|Pickup Truck": {
            "sku": "BRUSH_LIMBS|PICK-UP TRUCK",
            "name": "Dump Site - Brush and Limbs - Pickup Truck",
            "price": 35,
            "tax": 0
          },
          "Brush and Limbs|Dump Trl / Truck": {
            "sku": "BRUSH_LIMBS|DUMP TRL / TRK",
            "name": "Dump Site - Brush and Limbs - Dump Trailer or Truck",
            "price": 75,
            "tax": 0
          },
          "Brush and Limbs|TDM/Quad Axle": {
            "sku": "BRUSH_LIMBS|TDM / QUAD AXLE",
            "name": "Dump Site - Brush and Limbs - TDM or Quad Axle",
            "price": 110,
            "tax": 0
          },
          "Wood Chips|Pickup Truck": {
            "sku": "WOOD_CHIPS|PICK-UP TRUCK",
            "name": "Dump Site - Wood Chips - Pickup Truck",
            "price": 35,
            "tax": 0
          },
          "Wood Chips|Dump Trl / Truck": {
            "sku": "WOOD_CHIPS|DUMP TRL / TRK",
            "name": "Dump Site - Wood Chips - Dump Trailer or Truck",
            "price": 75,
            "tax": 0
          },
          "Wood Chips|TDM/Quad Axle": {
            "sku": "WOOD_CHIPS|TDM / QUAD AXLE",
            "name": "Dump Site - Wood Chips - TDM or Quad Axle",
            "price": 75,
            "tax": 0
          },
          "Logs <15\"|Pickup Truck": {
            "sku": "LOGS|PICK-UP TRUCK",
            "name": "Dump Site - Logs Under 15 Inches - Pickup Truck",
            "price": 35,
            "tax": 0
          },
          "Logs <15\"|Dump Trl / Truck": {
            "sku": "LOGS|DUMP TRL / TRK",
            "name": "Dump Site - Logs Under 15 Inches - Dump Trailer or Truck",
            "price": 130,
            "tax": 0
          },
          "Logs <15\"|TDM/Quad Axle": {
            "sku": "LOGS|TDM / QUAD AXLE",
            "name": "Dump Site - Logs Under 15 Inches - TDM or Quad Axle",
            "price": 190,
            "tax": 0
          }
        }
        """;

    public const string CompanyMappingsJson = """
        {
          "Green Hills Test": {
            "counterpointCustomerNumber": "101-10008"
          }
        }
        """;
}
