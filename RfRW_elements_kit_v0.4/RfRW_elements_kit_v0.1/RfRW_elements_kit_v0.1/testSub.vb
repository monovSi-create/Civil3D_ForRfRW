Imports DBTransactionManager = Autodesk.AutoCAD.DatabaseServices.TransactionManager
Imports Autodesk.Civil.ApplicationServices
Imports Autodesk.AutoCAD.DatabaseServices
Imports Autodesk.Civil.Runtime
Public Class testSub
    Inherits SATemplate
    Private Const dBlockLength = 0.4
    Private Const SideDefault = Utilities.Right
    Private Const dAssemblyNameDefault = "Участок"
    Protected Overrides Sub GetInputParametersImplement(corridorState As CorridorState)
        MyBase.GetInputParametersImplement(corridorState)

        ' define collection for long parameters in corridor
        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong

        ' define collection for double parameters in corridor
        Dim paramsDouble As ParamDoubleCollection
        paramsDouble = corridorState.ParamsDouble

        Dim paramsString As ParamStringCollection
        paramsString = corridorState.ParamsString
        ' Add input parameters we used in this script
        paramsLong.Add(Utilities.Side, SideDefault)
        paramsString.Add("AssemblyName", dAssemblyNameDefault)
        paramsDouble.Add("BlockLength", dBlockLength)

        Dim param As IParam

        param = paramsString.Add("AssemblyName", dAssemblyNameDefault)
        'paramsString.Add("Имя участка", dAssemblyNameDefault)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

        param = paramsLong.Add(Utilities.Side, SideDefault)
        'paramsLong.Add(Utilities.Side, SideDefault)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

        param = paramsDouble.Add("BlockLength", dBlockLength)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

    End Sub
    Protected Overrides Sub GetOutputParametersImplement(corridorState As CorridorState)
        MyBase.GetOutputParametersImplement(corridorState)

        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong

        Dim param As IParam

        param = paramsLong.Add("BlocksCount", 0)
        If Not param Is Nothing Then param.Access = ParamAccessType.Output
    End Sub
    Protected Overrides Sub DrawImplement(corridorState As CorridorState)

    End Sub
End Class
