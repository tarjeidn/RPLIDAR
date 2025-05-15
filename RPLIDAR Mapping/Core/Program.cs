#if GENERATE_PLANTUML
using PlantUmlClassDiagramGenerator;
[assembly: PlantUmlDiagram(
    IncludeMemberAccessibilities = Accessibilities.All)]
#endif


using var app = new RPLIDAR_Mapping.Core.Mapplication();
app.Run();
