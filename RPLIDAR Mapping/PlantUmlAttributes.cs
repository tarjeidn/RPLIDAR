using System;

namespace PlantUmlClassDiagramGenerator
{
  [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum | AttributeTargets.Assembly)]
  public sealed class PlantUmlDiagramAttribute : Attribute
  {
    public Accessibilities IncludeMemberAccessibilities { get; set; } = Accessibilities.NotSet;
    public Accessibilities ExcludeMemberAccessibilities { get; set; } = Accessibilities.NotSet;
    public AssociationTypes DisableAssociationTypes { get; set; } = AssociationTypes.NotSet;
  }

  [Flags]
  public enum Accessibilities
  {
    NotSet = 0x8000,
    None = 0,
    Public = 0x01,
    Protected = 0x02,
    Internal = 0x04,
    ProtectedInternal = 0x08,
    PrivateProtected = 0x10,
    Private = 0x20,
    All = Public | Protected | Internal | ProtectedInternal | PrivateProtected | Private
  }

  [Flags]
  public enum AssociationTypes
  {
    NotSet = 0x8000,
    None = 0,
    Inheritance = 0x01,
    Realization = 0x02,
    Property = 0x04,
    Field = 0x08,
    MethodParameter = 0x10,
    Nest = 0x20,
    All = Inheritance | Realization | Property | Field | MethodParameter | Nest
  }
}
