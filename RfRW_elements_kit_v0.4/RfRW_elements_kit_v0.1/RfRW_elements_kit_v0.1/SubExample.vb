Option Explicit On
Option Strict Off

Imports DBTransactionManager = Autodesk.AutoCAD.DatabaseServices.TransactionManager
Imports Autodesk.AutoCAD.ApplicationServices
Imports Autodesk.AutoCAD.DatabaseServices
Imports Autodesk.Civil.DatabaseServices
Imports Autodesk.Civil.Runtime

Public Class SubExample
    Inherits SATemplate
    ' *************************************************************************
    ' *************************************************************************
    ' *************************************************************************
    '          Name: BasicLane
    '
    '   Description: Creates a simple cross-sectional representation of a reinforcement soil wall composed of an array of geogrids.Attachment origin
    '                is at bottom.
    '
    ' Logical Names: Name                       Type       Optional  Description
    '                --------------------------------------------------------------
    '                TargetElevation             Profile    Yes       May be used to set height
    '                TargetOffset              Alignment    Yes       May be used to set width
    '
    '
    ' Input Parameters: Name                   Type    Optional    Default Value    Description
    '                -------------------------------------------------------------------------------------------
    '                FaceAngle              double      no          0                degrees of face element slope
    '                Side                   long        no          Right            specifies side to place SA on
    '                Width                  double      no          3.0              width of geogrids
    '                verticalStep           double      no          0.5              step of geogrid layer in vertical
    '                horizontalStep         double      no          0.0              step of layer in horizontal
    '                DrenageOffset          double      yes         0.3              use if target false
    '                DrenageElevation       long        yes          1               use if target false
    '                AssemblyName           string      no           1               use for point\link\shape names at some regions
    '
    'Output Parameters: Name               Type              Description
    '                ------------------------------------------------------------------
    '                None
    Private Const FaceAngleDefault = 0.01
    Private Const SideDefault = Utilities.Right  '"right"
    Private Const WidthDefault = 3.0
    Private Const vStepDefault = 0.5
    Private Const hStepDefault = 0.0
    Private Const AssemblyNameDefault = "Участок"
    Private Const dGridType1 = "RE580"
    Private Const dGridType2 = "RE570"
    Private Const dGridType3 = "RE560"
    Private Const dGridCount1 = 0
    Private Const dGridCount2 = 0
    Private Const dGridCount3 = 0
    'Добавляем информацию о входных параметрах
    Protected Overrides Sub GetInputParametersImplement(corridorState As CorridorState)
        MyBase.GetInputParametersImplement(corridorState)
        'создаем контейнеры для хранения инфы и присваиваем им значания соответствующих позиций из corridorState
        'for long
        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong
        'for double
        Dim paramsDouble As ParamDoubleCollection
        paramsDouble = corridorState.ParamsDouble
        'for string
        Dim paramsString As ParamStringCollection
        paramsString = corridorState.ParamsString
        'добавляем в эти контейнеры входные параметры которые будем использовать
        paramsLong.Add(Utilities.Side, SideDefault)
        paramsDouble.Add("Наклон лицевой грани", FaceAngleDefault)
        paramsDouble.Add("Длина георешеток", WidthDefault)
        paramsDouble.Add("Шаг георешеток", vStepDefault)
        paramsDouble.Add("Отскок слоя", hStepDefault)
        paramsString.Add("Имя участка", AssemblyNameDefault)
        'параметры георешеток (тип и кол-во слоев)
        paramsString.Add("Георешетка тип 1", dGridType1)
        paramsString.Add("Георешетка тип 2", dGridType2)
        paramsString.Add("Георешетка тип 3", dGridType3)
        paramsLong.Add("Кол-во слоев тип 1", dGridCount1)
        paramsLong.Add("Кол-во слоев тип 2", dGridCount2)
        paramsLong.Add("Кол-во слоев тип 3", dGridCount3)
    End Sub
    'при необходимости добавляем выходные параметры
    Protected Overrides Sub GetOutputParametersImplement(corridorState As CorridorState)
        MyBase.GetOutputParametersImplement(corridorState)
    End Sub
    'добавляем логические переменные
    Protected Overrides Sub GetLogicalNamesImplement(corridorState As CorridorState)
        MyBase.GetLogicalNamesImplement(corridorState)

        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong

        Dim ParamLong As ParamLong

        ParamLong = paramsLong.Add("Проектный профиль", ParamLogicalNameType.ElevationTarget)
        ParamLong.DisplayName = "Проектный профиль"

        ParamLong = paramsLong.Add("Граница засыпки", ParamLogicalNameType.OffsetTarget)
        ParamLong.DisplayName = "Граница засыпки"
    End Sub
    'создание логики построения и отрисовки элемента конструкции
    Protected Overrides Sub DrawImplement(corridorState As CorridorState)
        'объявим транзакцию
        Dim tm As DBTransactionManager
        tm = Autodesk.AutoCAD.DatabaseServices.HostApplicationServices.WorkingDatabase.TransactionManager
        'собираем ранее введеные параметры из corridorState
        'целевой профиль
        Dim oParamsElevationTarget As ParamElevationTargetCollection
        oParamsElevationTarget = corridorState.ParamsElevationTarget
        'целевой отступ (трасса)
        Dim oParamsOffsetTarget As ParamOffsetTargetCollection
        oParamsOffsetTarget = corridorState.ParamsOffsetTarget
        'коллекции переменных
        Dim paramsLong As ParamLongCollection
        paramsLong = corridorState.ParamsLong

        Dim paramsDouble As ParamDoubleCollection
        paramsDouble = corridorState.ParamsDouble

        Dim paramsString As ParamStringCollection
        paramsString = corridorState.ParamsString
        '-----------------------------------------
        'создаем переменные, которые будут принимать значения от введенных параметров, для использования в нашем коде
        'определяем сторону в которую строится конструкция
        Dim side As Long
        Try
            side = paramsLong.Value(Utilities.Side)
        Catch
            side = SideDefault
        End Try
        '------------------------------
        'переменная, которую будем использовать для отзеркаливания 
        Dim flip As Double
        flip = 1.0#
        If side = Utilities.Left Then
            flip = -1.0#
        End If
        '------------------------------
        Dim width As Double
        Try
            width = paramsDouble.Value("Длина георешеток")
        Catch
            width = WidthDefault
        End Try
        '------------------------------
        Dim faceAngle As Double
        Try
            faceAngle = paramsDouble.Value("Наклон лицевой грани")
        Catch
            faceAngle = FaceAngleDefault
        End Try
        '------------------------------
        Dim gStep As Double
        Try
            gStep = paramsDouble.Value("Шаг георешеток") '(он же высота слоя)
        Catch
            gStep = vStepDefault
        End Try
        '------------------------------
        Dim hStep As Double
        Try
            hStep = paramsDouble.Value("Отскок слоя")
        Catch
            hStep = hStepDefault
        End Try
        '------------------------------
        Dim oAssemblyName As String
        Try
            oAssemblyName = paramsString.Value("Имя участка") '(пригодится для конструкций из несколькиз областей)
        Catch
            oAssemblyName = AssemblyNameDefault
        End Try
        '------------------------------
        Dim oGridType1 As String
        Try
            oGridType1 = paramsString.Value("Георешетка тип 1")
        Catch
            oGridType1 = dGridType1
        End Try
        '------------------------------
        Dim oGridType2 As String
        Try
            oGridType2 = paramsString.Value("Георешетка тип 2")
        Catch
            oGridType2 = dGridType2
        End Try
        '------------------------------
        Dim oGridType3 As String
        Try
            oGridType3 = paramsString.Value("Георешетка тип 3")
        Catch
            oGridType3 = dGridType1
        End Try
        '------------------------------
        Dim oGridCount1 As Long
        Try
            oGridCount1 = paramsLong.Value("Кол-во слоев тип 1")
        Catch
            oGridCount1 = dGridCount1
        End Try
        '------------------------------
        Dim oGridCount2 As Long
        Try
            oGridCount2 = paramsLong.Value("Кол-во слоев тип 2")
        Catch
            oGridCount2 = dGridCount2
        End Try
        '------------------------------
        Dim oGridCount3 As Long
        Try
            oGridCount3 = paramsLong.Value("Кол-во слоев тип 3")
        Catch
            oGridCount3 = dGridCount3
        End Try
        '------------------------------
        ' проверка введенных пользователем значений и создание их ограничений
        ' например минимум по длине 
        If width < 3 Then
            Utilities.RecordError(corridorState, CorridorError.ValueTooSmall, "Длина георешеток", "Geogrid")
            width = WidthDefault
        End If
        ' или ограничение шага геоармирования
        If gStep <= 0 Then
            Utilities.RecordError(corridorState, CorridorError.ValueShouldNotBeLessThanOrEqualToZero, "Шаг георешеток", "Geogrid")
            gStep = vStepDefault
        End If
        'находим трассу и ориджин для рассматриваемого сечения
        Dim oOrigin As New PointInMem '(это точка в рассматриваемом сечении, относительно которой строится текущий элемент констр. при переносе элемента, ориджин помогает не потеряться в сечении)
        Dim oCurrentAlignmentId As ObjectId
        Utilities.GetAlignmentAndOrigin(corridorState, oCurrentAlignmentId, oOrigin)
        If corridorState.Mode = CorridorMode.Design Then 'при построении элементов внутри коридора
            'найдем цели для построения элемента конструкции
            ' высоту конструкции
            Dim elevationTarget As SlopeElevationTarget
            Try
                elevationTarget = oParamsElevationTarget.Value("Проектный профиль")
            Catch
                elevationTarget = Nothing
            End Try
            Dim hasWallElevationProfile As Boolean
            hasWallElevationProfile = False
            Dim wallHeight As Double
            If Not elevationTarget Is Nothing Then
                Try
                    wallHeight = elevationTarget.GetElevation(oCurrentAlignmentId, corridorState.CurrentStation) - oOrigin.Elevation
                    hasWallElevationProfile = True
                Catch
                    Utilities.RecordWarning(corridorState, CorridorError.LogicalNameNotFound, "Проектный профиль", "RetainWallVertical")
                End Try
            End If

            ' и отступ грунта засыпки
            Dim offsetTarget As WidthOffsetTarget
            Try
                offsetTarget = oParamsOffsetTarget.Value("Граница засыпки")
            Catch
                offsetTarget = Nothing
            End Try
            Dim hasWallOffsetTarget As Boolean
            hasWallOffsetTarget = False

            Dim xOffset As Double
            Dim yOffset As Double
            Dim soilOffset As Double

            If Not offsetTarget Is Nothing Then
                Try
                    Utilities.CalcAlignmentOffsetToThisAlignment(oCurrentAlignmentId, corridorState.CurrentStation, offsetTarget, soilOffset, xOffset, yOffset)
                    hasWallOffsetTarget = True
                    soilOffset = soilOffset - oOrigin.Offset
                Catch
                    Utilities.RecordWarning(corridorState, CorridorError.LogicalNameNotFound, "Граница засыпки", "RetainWallHorizontal")
                End Try
            End If
            '---------------------------
            'отсюда начинается обработка введеных значений и построение конструкции
            '---------------------------
            wallCreate(corridorState, wallHeight, soilOffset, width, gStep, hStep, faceAngle, flip, oAssemblyName, oGridCount1, oGridCount2, oGridCount3, oGridType1, oGridType2, oGridType3)
        Else 'при построении элементов на виде конструкции
            Dim wallHeight As Double
            Dim soilOffset As Double = width + 1.0
            Dim totalCount = oGridCount1 + oGridCount2 + oGridCount3
            If totalCount = 0 Then
                wallHeight = 2.0
            Else
                wallHeight = totalCount * gStep
            End If
            wallCreate(corridorState, wallHeight, soilOffset, width, gStep, hStep, faceAngle, flip, oAssemblyName, oGridCount1, oGridCount2, oGridCount3, oGridType1, oGridType2, oGridType3)
        End If

        Dim param As IParam
        param = paramsLong.Add(Utilities.Side, SideDefault)
        param = paramsDouble.Add("Наклон лицевой грани", FaceAngleDefault)
        param = paramsDouble.Add("Длина георешеток", WidthDefault)
        param = paramsDouble.Add("Шаг георешеток", vStepDefault)
        param = paramsDouble.Add("Отскок слоя", hStepDefault)
        param = paramsString.Add("Имя участка", AssemblyNameDefault)
        '
        param = paramsString.Add("Георешетка тип 1", dGridType1)
        param = paramsString.Add("Георешетка тип 2", dGridType2)
        param = paramsString.Add("Георешетка тип 3", dGridType3)
        param = paramsLong.Add("Кол-во слоев тип 1", dGridCount1)
        param = paramsLong.Add("Кол-во слоев тип 2", dGridCount2)
        param = paramsLong.Add("Кол-во слоев тип 3", dGridCount3)
    End Sub
    'метод для создания точек вставки слоев
    Private Sub wallCreate(ByVal corridorState As CorridorState,
                           ByVal wallHeight As Double,
                           ByVal soilWidth As Double,
                           ByVal gridWidth As Double,
                           ByVal gridStep As Double,
                           ByVal horizontalStep As Double,
                           ByVal faceAngle As Double,
                           ByVal flipValue As Double,
                           ByVal assemblyName As String,
                           ByVal gridCount1 As Long,
                           ByVal gridCount2 As Long,
                           ByVal gridCount3 As Long,
                           ByVal gridType1 As String,
                           ByVal gridType2 As String,
                           ByVal gridType3 As String)
        'для первой отладки создадим видимые точки
        Dim testPoints As PointCollection
        testPoints = corridorState.Points
        Dim testPoint As Point

        'далее в качестве точек вставки используем "точки из памяти"
        Dim insertPoint As New PointInMem

        Dim elevatP As Double = 0.0 'переменные для записи значений отметки и отступа
        Dim offsetP As Double = 0.0
        Dim dX As Double = (horizontalStep + gridStep * Math.Tan(faceAngle * Math.PI / 180)) * flipValue 'отступ для каждого вышележащего ряда (в метрах) 
        'определим кол-во слоев
        Dim layers As Integer
        layers = wallHeight * 1000 \ gridStep * 1000
        'определим остаток сверху
        Dim reminder As Double
        reminder = wallHeight Mod gridStep
        'цикл для создания армогрунтовой стенки
        Dim soilName As String = assemblyName + " Soil"
        Dim drenageName As String = assemblyName + " Crushed Stone"
        Dim i As Integer = 1
        Do While layers >= i
            'testPoint = testPoints.Add(offsetP, elevatP, i.ToString())
            insertPoint.Offset = offsetP
            insertPoint.Elevation = elevatP

            Dim linkName As String
            'логика присваивания названия слоя
            If i <= gridCount1 Then
                linkName = assemblyName + " " + i.ToString + gridType1
                createGeogrid(corridorState, assemblyName, gridWidth, flipValue, insertPoint)
            ElseIf gridCount1 < i And i <= gridCount1 + gridCount2 Then
                linkName = assemblyName + " " + i.ToString + gridType2
                createGeogrid(corridorState, assemblyName, gridWidth, flipValue, insertPoint)
            ElseIf gridCount1 + gridCount2 < i And i <= gridCount1 + gridCount2 + gridCount3 Then
                linkName = assemblyName + " " + i.ToString + gridType3
                createGeogrid(corridorState, assemblyName, gridWidth, flipValue, insertPoint)
            End If

            createSoilLayer(corridorState, soilName, drenageName, soilWidth, gridStep, faceAngle, flipValue, insertPoint)

            elevatP += gridStep
            offsetP += dX
            i += 1
        Loop
        insertPoint.Offset = offsetP
        insertPoint.Elevation = elevatP
        createSoilLayer(corridorState, soilName, drenageName, soilWidth, reminder, faceAngle, flipValue, insertPoint)
    End Sub
    'метод для создания одной георешетки
    Private Sub createGeogrid(ByVal corridorState As CorridorState,
                              ByVal linkName As String,
                              ByVal geogridWidth As Double,
                              ByVal flipValue As Double,
                              ByVal pointToInsert As PointInMem)
        '---------------------------------------------------------
        ' создание точек и связи между ними
        '---------------------------------------------------------
        Dim geogridPoints As PointCollection
        geogridPoints = corridorState.Points

        Dim geogridLinks As LinkCollection
        geogridLinks = corridorState.Links

        Dim gridPoint1 As Point
        Dim gridPoint2 As Point
        Dim gridLink As Link

        gridPoint1 = geogridPoints.Add(pointToInsert.Offset * flipValue, pointToInsert.Elevation, linkName + "1")
        gridPoint2 = geogridPoints.Add((pointToInsert.Offset + geogridWidth) * flipValue, pointToInsert.Elevation, linkName + "2")
        gridLink = geogridLinks.Add(gridPoint1, gridPoint2, linkName)

    End Sub
    'метод создания одного слоя засыпки
    Private Sub createSoilLayer(ByVal corridorState As CorridorState,
                                ByVal soilShapeName As String,
                                ByVal drenageShapeName As String,
                                ByVal width As Double,
                                ByVal layerHeight As Double,
                                ByVal faceAngle As Double,
                                ByVal flipValue As Double,
                                ByVal pointToInsert As PointInMem)
        'объявляем коллекции элементов
        Dim Points As PointCollection
        Points = corridorState.Points
        Dim Links As LinkCollection
        Links = corridorState.Links
        Dim Shapes As ShapeCollection
        Shapes = corridorState.Shapes
        'вычисляем вспомогательные параметры
        Dim dOffset = layerHeight * Math.Tan(faceAngle * Math.PI / 180)
        Dim stoneSlope = 1 / 1.5
        Dim stoneTopWidth = 0.3
        'строим слой по точкам
        Dim Point1 As Point = Points.Add(pointToInsert.Offset * flipValue, pointToInsert.Elevation, "")
        Dim Point2 As Point = Points.Add(Point1.Offset + dOffset * flipValue, Point1.Elevation + layerHeight, "")
        Dim Point3 As Point = Points.Add(Point2.Offset + stoneTopWidth * flipValue, Point2.Elevation, "")
        Dim Point4 As Point = Points.Add(Point3.Offset + layerHeight * flipValue / stoneSlope, Point1.Elevation, "")
        Dim Point5 As Point = Points.Add(Point1.Offset + width * flipValue, Point1.Elevation, "")
        Dim Point6 As Point = Points.Add(Point2.Offset + width * flipValue, Point2.Elevation, "")

        Dim Link1 As Link = Links.Add(Point1, Point2, "")
        Dim Link2 As Link = Links.Add(Point2, Point3, "")
        Dim Link3 As Link = Links.Add(Point3, Point4, "")
        Dim Link4 As Link = Links.Add(Point4, Point5, "")
        Dim Link5 As Link = Links.Add(Point5, Point6, "")
        Dim Link6 As Link = Links.Add(Point1, Point4, "")
        Dim Link7 As Link = Links.Add(Point3, Point6, "")

        Dim gLinks As Link() = {Link1, Link2, Link3, Link6}
        Dim drenShape As Autodesk.Civil.DatabaseServices.Shape = Shapes.Add(gLinks, drenageShapeName)
        Dim sLinks As Link() = {Link3, Link4, Link5, Link7}
        Dim soilShape As Autodesk.Civil.DatabaseServices.Shape = Shapes.Add(sLinks, soilShapeName)

    End Sub
End Class
