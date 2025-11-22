namespace CMS.Domain.Entities;

public enum UserType { Admin, Citizen, Employee, DepartmentManager }
// Merged logic: This enum covers both employee department and managed department
public enum Department { Electricity, Water, Sanitation, Roads, Building, Parks, General }