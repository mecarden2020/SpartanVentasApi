namespace SpartanVentasApi.Models.Sap
{
    public class BpAddressDto
    {
        public string AddressName { get; set; } = string.Empty;
        public string AddressType { get; set; } = string.Empty; // "bo_BillTo" o "bo_ShipTo"
        public string Street { get; set; } = string.Empty;
        public string Block { get; set; } = string.Empty;
        public string City { get; set; } = string.Empty;
        public string ZipCode { get; set; } = string.Empty;
    }
    public class BpContactEmployeeDto
    {
        public int InternalCode { get; set; }
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
    }
    public class BusinessPartnerDto
    {
        public string CardCode { get; set; } = string.Empty;
        public string CardName { get; set; } = string.Empty;
        public int? SalesPersonCode { get; set; }
        public string? BillToDefault { get; set; }
        public string? ShipToDefault { get; set; }
        public List<BpAddressDto> BPAddresses { get; set; } = new();
        public List<BpContactEmployeeDto> ContactEmployees { get; set; } = new();
    }
}
