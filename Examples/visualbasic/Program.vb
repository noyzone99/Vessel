Imports System.IO
Imports System.Text
Imports Vessel

Module Program
    Sub Main()
        RunAsync().GetAwaiter().GetResult()
    End Sub

    Private Async Function RunAsync() As Task
        Const message As String = "Hello from Vessel!"
        Using storage As New VesselStorage("my-store")
            Using input As New MemoryStream(Encoding.UTF8.GetBytes(message))
                Dim metadata = Await storage.PutAsync("documents/hello.txt", input, overwrite:=True)

                Using content = storage.GetStream(metadata.Key)
                    Using reader As New StreamReader(content, Encoding.UTF8, leaveOpen:=True)
                        Dim stored = Await reader.ReadToEndAsync()
                        If stored <> message Then
                            Throw New InvalidDataException("Stored text differs from the original.")
                        End If

                        Console.WriteLine(stored)
                    End Using
                End Using
            End Using
        End Using
    End Function
End Module
