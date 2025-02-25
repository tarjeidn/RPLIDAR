using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace RPLIDAR_Mapping.Utilities
{


  public static class Logger
  {
    public static void Log(string message,
                           [CallerFilePath] string filePath = "",
                           [CallerLineNumber] int lineNumber = 0,
                           [CallerMemberName] string memberName = "")
    {
      var fileName = System.IO.Path.GetFileName(filePath); // Extract file name from path
      Debug.WriteLine($"[{fileName}:{lineNumber} {memberName}] {message}");
    }
  }

}
