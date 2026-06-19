uses MLABC;
uses TestHelpers in '..\TestHelpers.pas';

begin
  var df := new DataFrame;
  df.AddDateTimeColumn('created_at', Arr(
    new System.DateTime(2024, 1, 15),
    new System.DateTime(2024, 1, 16, 12, 30, 0)
  ));

  var oldOut := System.Console.Out;
  var sw := new System.IO.StringWriter;
  System.Console.SetOut(sw);
  try
    df.Print;
  finally
    System.Console.SetOut(oldOut);
  end;

  var s := sw.ToString;
  Check(s.Contains('2024-01-15'), 'Default Print must show date-only value');
  Check(s.Contains('2024-01-16 12:30:00'), 'Default Print must show date and time');
  Check(not s.Contains('2024-01-15 00:00:00'), 'Default Print must omit zero time');

  sw := new System.IO.StringWriter;
  System.Console.SetOut(sw);
  try
    df.Print(dateTimeFormat := 'yyyy/MM/dd');
  finally
    System.Console.SetOut(oldOut);
  end;

  s := sw.ToString;
  Check(s.Contains('2024/01/15'), 'Custom Print format must be applied');
  Check(s.Contains('2024/01/16'), 'Custom Print format must be applied to all DateTime values');
end.
