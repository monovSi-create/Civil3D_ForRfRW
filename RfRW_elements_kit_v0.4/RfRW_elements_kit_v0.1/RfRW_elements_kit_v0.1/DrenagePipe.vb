Option Strict Off
Imports DBTransactionManager = Autodesk.AutoCAD.DatabaseServices.TransactionManager
Imports Autodesk.AutoCAD.Internal
Imports Autodesk.Civil.DatabaseServices
Imports Autodesk.Civil.Runtime
Imports Autodesk.AutoCAD.DatabaseServices
Imports System.Math
Imports System.Security.Policy
Imports Autodesk.Civil.ApplicationServices
Public Class DrenagePipe
    Inherits SATemplate
    Private Const SideDefault = Utilities.Right
    Private Const dPipeSlope = 0.05
    Private Const dPipeStep = 5.0
    Private Const dPipeDiametr = 0.16
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


        paramsDouble.Add("Уклон дренажной трубы", dPipeSlope)
        paramsDouble.Add("Шаг дренажных выпусков", dPipeStep)
        paramsDouble.Add("Диаметр дренажной трубы", dPipeDiametr)
        paramsString.Add("Имя участка", dAssemblyNameDefault)

        Dim param As IParam
        param = paramsDouble.Add("Уклон дренажной трубы", dPipeSlope)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput
        param = paramsDouble.Add("Шаг дренажных выпусков", dPipeStep)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput
        param = paramsDouble.Add("Диаметр дренажной трубы", dPipeDiametr)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput
        param = paramsLong.Add(Utilities.Side, SideDefault)
        paramsLong.Add(Utilities.Side, SideDefault)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput
    End Sub

    Protected Overrides Sub DrawImplement(corridorState As CorridorState)
        Dim tm As DBTransactionManager
        tm = Autodesk.AutoCAD.DatabaseServices.HostApplicationServices.WorkingDatabase.TransactionManager
        Dim paramsDouble As ParamDoubleCollection
        paramsDouble = corridorState.ParamsDouble
        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong

        Dim side As Long
        Try
            side = paramsLong.Value(Utilities.Side)
        Catch
            side = SideDefault
        End Try
        '----------------------------------------
        'flip about Y axis
        Dim flip As Double
        flip = 1.0#
        If side = Utilities.Left Then
            flip = -1.0#
        End If
        '----------------------------------------
        Dim oPipeStep As Double
        Try
            oPipeStep = paramsDouble.Value("Шаг дренажных выпусков") / 2
        Catch
            oPipeStep = dPipeStep / 2
        End Try
        '-----------------------
        Dim oPipeSlope As Double
        Try
            oPipeSlope = paramsDouble.Value("Уклон дренажной трубы")
        Catch
            oPipeSlope = dPipeSlope
        End Try
        '-----------------------
        Dim oPipeD As Double
        Try
            oPipeD = paramsDouble.Value("Диаметр дренажной трубы")
        Catch
            oPipeD = dPipeDiametr
        End Try
        '-----------------------------------------------

        If corridorState.Mode = CorridorMode.Design Then
            Dim dPoint As Point
            'добавляем сечения в характерных точках
            If corridorState.CurrentStation = corridorState.CurrentRegionStartStation Then
                createAddStations(tm, corridorState, oPipeStep, oPipeSlope)
            End If
            Dim dPointColl As PointCollection
            dPointColl = corridorState.Points
            createPipeAxis(corridorState, oPipeStep, oPipeSlope, dPointColl, dPoint, oPipeD, flip)

            Dim pipePointColl As PointCollection
            pipePointColl = corridorState.Points
            Dim pipeLinkColl As LinkCollection
            pipeLinkColl = corridorState.Links
            Dim pipeShapeColl As ShapeCollection
            pipeShapeColl = corridorState.Shapes
            createPipe(dPoint, oPipeD, pipePointColl, pipeLinkColl, pipeShapeColl)

            Dim drenageOffset As Double
            Dim layerHeight As Double
            Dim layerSlope As Double
            Dim faceSlope As Double

            Dim corridor As Corridor
            corridor = tm.GetObject(corridorState.CurrentCorridorId, OpenMode.ForWrite)
            Dim baselines As BaselineCollection
            baselines = corridor.Baselines
            Dim baseline As Baseline
            For Each b As Baseline In baselines
                If corridorState.CurrentProfileId = b.ProfileId Then
                    baseline = b
                    Dim regs As BaselineRegionCollection
                    regs = baseline.BaselineRegions
                    For Each reg As BaselineRegion In regs
                        If reg.StartStation = corridorState.CurrentRegionStartStation Or reg.EndStation = corridorState.CurrentRegionEndStation Then
                            Dim assembly As Assembly
                            assembly = tm.GetObject(reg.AssemblyId, OpenMode.ForRead)
                            Dim asGrColl As AssemblyGroupCollection
                            asGrColl = assembly.Groups
                            For Each group In asGrColl
                                For Each subassemblyId In group.GetSubassemblyIds
                                    Dim subassembly As Subassembly
                                    subassembly = tm.GetObject(subassemblyId, OpenMode.ForRead)
                                    Dim sName = subassembly.Name
                                    If sName = "Soil" Then
                                        Dim outputParams = subassembly.ParamsDouble
                                        For Each outParam In outputParams
                                            If outParam.DisplayName = "Ширина дренажных призм" Then
                                                drenageOffset = outParam.Value
                                            ElseIf outParam.DisplayName = "Шаг георешеток" Then
                                                layerHeight = outParam.Value
                                            ElseIf outParam.DisplayName = "Заложение дренажных призм" Then
                                                layerSlope = outParam.Value
                                            ElseIf outParam.DisplayName = "Наклон лицевой грани" Then
                                                faceSlope = outParam.Value
                                            End If
                                        Next
                                    End If
                                Next
                            Next
                        End If
                    Next
                End If
            Next
            createGeomembrane(corridorState, dPoint, oPipeD, drenageOffset, layerHeight, layerSlope, faceSlope, flip)

        Else 'layout mode
            Dim dPointColl As PointCollection
            dPointColl = corridorState.Points
            Dim dPoint As Point
            createPipeAxis(corridorState, oPipeStep, oPipeSlope, dPointColl, dPoint, oPipeD, flip)

            Dim pipePointColl As PointCollection
            pipePointColl = corridorState.Points
            Dim pipeLinkColl As LinkCollection
            pipeLinkColl = corridorState.Links
            Dim pipeShapeColl As ShapeCollection
            pipeShapeColl = corridorState.Shapes
            createPipe(dPoint, oPipeD, pipePointColl, pipeLinkColl, pipeShapeColl)

            'createGeomembrane(corridorState, dPoint, oPipeD, drenageOffset, layerHeight, layerSlope, faceSlope, flip)
        End If
        Dim param As IParam
        param = paramsDouble.Add("Уклон дренажной трубы", dPipeSlope)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput
        param = paramsDouble.Add("Шаг дренажных выпусков", dPipeStep)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput
        param = paramsDouble.Add("Диаметр дренажной трубы", dPipeDiametr)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput
        param = paramsLong.Add(Utilities.Side, SideDefault)
        paramsLong.Add(Utilities.Side, SideDefault)
        If Not param Is Nothing Then param.Access = ParamAccessType.InputAndOutput

    End Sub
    Private Sub createPipeAxis(ByVal corridorState As CorridorState, ByVal pipeStep As Double, ByVal pipeSlope As Double, ByVal pointCollection As PointCollection, ByRef axisPoint As Point, pipeDiam As Double, flip As Double)

        'находим переменные задающие положение трубы в пространстве (вертикальное смещение)
        'максимально возможная отметка относительно нуля 
        Dim tH = pipeStep * pipeSlope
        'дельта отметки для рассматриваемого сечения
        Dim dH = ((corridorState.CurrentStation - corridorState.CurrentRegionStartStation) Mod 2 * pipeStep) * pipeSlope
        'направление в котором стоит откладывать дельту в текущем сечении
        Dim dir = Math.Sin(PI / 2 + ((corridorState.CurrentStation - corridorState.CurrentRegionStartStation) \ pipeStep) * PI)
        'определяем отметку

        Dim oPipeElev As Double
        oPipeElev = Math.Abs(tH - dH) + pipeDiam / 2

        axisPoint = pointCollection.Add((pipeDiam / 2 + 0.05) * flip, oPipeElev, "Ось дренажной трубы")

    End Sub
    Private Sub createPipe(cPoint As Point, pDiam As Double, pipePoints As PointCollection, pipeLinks As LinkCollection, pipeShape As ShapeCollection)
        Dim P1 As Point
        Dim P2 As Point
        Dim L1 As Link
        Dim S1 As Autodesk.Civil.DatabaseServices.Shape

        Dim i As Double = 0
        Dim circleStep = PI / 6
        Dim links As New List(Of Link)
        Do While i < 2 * PI
            If i <> 1.5 * PI Then
                P1 = pipePoints.Add(cPoint.Offset + Math.Cos(i) * pDiam / 2, cPoint.Elevation + Math.Sin(i) * pDiam / 2, "")
                P2 = pipePoints.Add(cPoint.Offset + Math.Cos(i + circleStep) * pDiam / 2, cPoint.Elevation + Math.Sin(i + circleStep) * pDiam / 2, "")
                L1 = pipeLinks.Add(P1, P2, "")
            Else
                P1 = pipePoints.Add(cPoint.Offset + Math.Cos(i) * pDiam / 2, cPoint.Elevation + Math.Sin(i) * pDiam / 2, "Низ дренажной трубы")
                P2 = pipePoints.Add(cPoint.Offset + Math.Cos(i + circleStep) * pDiam / 2, cPoint.Elevation + Math.Sin(i + circleStep) * pDiam / 2, "")
                L1 = pipeLinks.Add(P1, P2, "")
            End If
            links.Add(L1)
            i += circleStep
        Loop
        S1 = pipeShape.Add(links.ToArray(), "Дренажная труба")
    End Sub
    Private Sub createGeomembrane(corridorState As CorridorState, cPoint As Point, pDiam As Double, drenageOffset As Double, layerHeight As Double, layerSlope As Double, faceAngle As Double, flip As Double)

        Dim membranePoints As PointCollection
        membranePoints = corridorState.Points
        Dim membraneLinks As LinkCollection
        membraneLinks = corridorState.Links

        Dim P1 As Point
        Dim P2 As Point
        Dim P3 As Point
        Dim P4 As Point
        Dim L1 As Link
        Dim L2 As Link
        Dim L3 As Link

        Dim membraneLow = cPoint.Elevation - pDiam / 2
        Dim membraneDeltaH = layerHeight - membraneLow
        Dim layerTan = 1 / layerSlope
        Dim faceSlope = faceAngle * Math.PI / 180
        Dim faceTan = Tan(faceSlope) * flip

        P1 = membranePoints.Add((drenageOffset + layerHeight / layerTan) * flip, layerHeight, "")
        P2 = membranePoints.Add((drenageOffset + membraneLow / layerTan) * flip, membraneLow, "")
        P3 = membranePoints.Add(membraneLow * faceTan * flip, membraneLow, "")
        P4 = membranePoints.Add((membraneLow + membraneDeltaH) * faceTan * flip, layerHeight, "")

        L1 = membraneLinks.Add(P1, P2, "geomembrane1")
        L2 = membraneLinks.Add(P2, P3, "geomembrane1")
        L3 = membraneLinks.Add(P3, P4, "geomembrane2")

    End Sub
    Public Sub createAddStations(tm As DBTransactionManager, corridorState As CorridorState, pipeStep As Double, pipeSlope As Double)
        Dim origin As New PointInMem
        Dim alignmentId As ObjectId
        Utilities.GetAlignmentAndOrigin(corridorState, alignmentId, origin)
        'пробегаем по всей области и находи пикеты "скачка" блоков
        Dim startSt = corridorState.CurrentRegionStartStation
        Dim stateStep As Double = 0.001
        Dim endSt = corridorState.CurrentRegionEndStation
        Dim stationCurr = startSt
        Dim sectionsToAdd As New List(Of Double)
        Dim tH = pipeStep * pipeSlope
        Do While stationCurr < endSt
            Dim dH = ((stationCurr - corridorState.CurrentRegionStartStation) Mod pipeStep) * pipeSlope
            Dim remainder = tH - dH
            If Math.Abs(remainder) < 0.0001 Then
                sectionsToAdd.Add(stationCurr)
                stationCurr += 0.1
            End If
            stationCurr += stateStep
        Loop

        Dim corridor As Corridor
        corridor = tm.GetObject(corridorState.CurrentCorridorId, OpenMode.ForWrite)
        Dim baselines As BaselineCollection
        baselines = corridor.Baselines
        Dim baseline As Baseline
        For Each b As Baseline In baselines
            If corridorState.CurrentProfileId = b.ProfileId Then
                baseline = b
                Dim regs As BaselineRegionCollection
                regs = baseline.BaselineRegions
                For Each reg As BaselineRegion In regs
                    If reg.StartStation = corridorState.CurrentRegionStartStation Or reg.EndStation = corridorState.CurrentRegionEndStation Then
                        'очищаем дополнительные сечения
                        Dim settings = reg.AppliedAssemblySetting
                        Dim infos = settings.AdditionalAppliedAssemblies
                        For Each info In infos
                            Dim description = "доп.сечения дренажной трубы " + baseline.Name
                            If info.Description = description Then
                                reg.DeleteStation(info.Station)
                            End If
                        Next
                        'добавляем новые сечения 
                        Dim assemblyStations As Double()
                        assemblyStations = reg.AppliedAssemblies.Stations
                        'если в точке нет сечения - создаем дополнительное
                        Dim diff = sectionsToAdd.Except(assemblyStations)
                        For Each station In diff
                            Try
                                reg.AddStation(station, "доп.сечения дренажной трубы " + baseline.Name)
                            Catch

                            End Try
                        Next
                    End If
                Next
            End If
        Next
    End Sub
End Class
